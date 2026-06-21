# TaskGraph 委托与对话内编排方案评估

> 状态: 评估完成，建议修订原方案后再实施
> 评估日期: 2026-06-21
> 评估对象: `Docs/working/TaskGraph-委托与对话内编排-方案.md`

---

## 1. 结论

该方案可以作为 v3 设计基线，但**不建议原样直接实施**。

整体方向是成立的:

- 将 v3 聚焦在“节点级委托策略”和“对话内 TaskGraph”两件事，范围控制合理
- 对当前代码基线的判断大体准确
- 向后兼容思路清晰，默认回落到 `NewSession` 的策略可行

但方案中有 3 个关键语义问题需要先修正，否则会导致实现抽象与实际能力不一致:

1. `InSessionSubagent` 的定义与当前可实现路径不一致
2. `ChildSession` 被赋予了过强的“共享上下文/节省 token”含义
3. Chat 内嵌执行与现有 Chat 会话模型之间缺少明确的所有权边界

---

## 2. 现状核对

基于当前代码，以下判断成立:

- `OpenCode.Client` 已暴露 `Session.ParentID`
- `SessionCreateRequest` 已支持 `ParentID`
- `OpenCodeAgentGateway` 已使用 `children` 接口获取子会话活动
- 当前 `TaskGraphExecutor` 仍是“每节点固定新建 session 再执行”
- 当前 `IAgentGateway` 只暴露了:
  - `CreateSessionAsync(...)`
  - `SendMessageAsync(...)`
  - `GetSubagentActivitiesAsync(...)`

这意味着:

- `ChildSession` 方向具备落地基础
- 真正意义上的 “in-session subagent” 能力，在当前项目接口层**尚未被明确建模**

---

## 3. 主要问题

### 3.1 `InSessionSubagent` 定义与实现路径冲突

原方案一方面将 `InSessionSubagent` 定义为:

- 使用 OpenCode 原生 task / 子 agent 机制
- 共享父 session 完整上下文
- 父 session 可看到完整推理过程

但另一方面又写到:

- v3 简化为“同一个 sessionId 上直接 `SendMessage`”

这两者不是同一件事。

在当前代码里，直接对同一个 `sessionId` 调 `SendMessageAsync(...)`，语义上只是:

- 在当前会话中继续发一条消息
- 由当前主会话继续回答

它**不天然等价于**:

- 独立的子代理调用
- 清晰可追踪的子任务边界
- 可单独订阅的子执行流
- 可稳定复用现有 `SubagentActivities` 视图

因此，原方案对 `InSessionSubagent` 的命名和说明偏乐观。

**建议修订**

- 将 v3 中的 `InSessionSubagent` 降级为更保守的语义，例如:
  - `InSessionStep`
  - `InSessionExecution`
- 明确其含义是:
  - 复用当前 chat session 执行节点
  - 不新建独立 session
  - 允许流式输出进入当前会话域
- 不要在 v3 文档中宣称其已等价于“原生子 agent”

---

### 3.2 `ChildSession` 的“共享上下文”假设证据不足

原方案多处将 `ChildSession` 描述为:

- 与父任务有上下文关联
- 共享父项目/父背景
- 可减少重复读取项目上下文
- 特别适合 bug list 模板复用背景

但从当前已确认事实看，项目目前只能证明:

- 子 session 可以声明 `ParentID`
- 父 session 可以通过 `/children` 枚举子 session

**尚不能证明** 以下能力一定成立:

- 自动继承父消息上下文
- 自动继承父工具上下文
- 自动复用父步骤的分析结果
- 自动减少 token 消耗

因此，`ChildSession` 更稳妥的语义应当是:

- “带父子归属关系的独立 session”

而不是:

- “低成本共享上下文 session”

**建议修订**

- 将所有“共享父上下文”“节省 token”的描述改为“待验证假设”
- 在方案中加入实施前验证项:
  - 验证 ParentID 对消息上下文继承行为
  - 验证工具/权限是否继承
  - 验证 token 成本是否确实下降
- Bug 列表模板先按“可归属、可分组观察”设计，不先绑定“节省 token”收益

---

### 3.3 Chat 执行边界尚未收口

原方案新增了 `ConversationSessionId`，并设想:

- `Inline` / `InSessionSubagent` 复用当前 chat session
- 执行期间禁用 composer
- 执行结束后将结果摘要插回 chat 流

这个方向合理，但目前缺少一个关键设计:

- **谁拥有 chat session 的写入控制权**

当前 `TaskGraphExecutor` 是独立执行器，它并不知道 chat 会话生命周期，只知道:

- 创建 session
- 发消息
- 取消息

如果不先定义执行边界，后续容易出现这些问题:

- Chat 用户消息与图节点执行消息并发写入同一 session
- 结果摘要和原始节点输出混在一起
- `ChatWorkspaceViewModel` 和 `TaskGraphExecutor` 互相持有过多状态

**建议修订**

- 在执行请求层显式建模:
  - `ConversationSessionId`
  - `ExecutionPresentationMode`
  - `AllowInlineExecution`
- 由 `ChatWorkspaceViewModel` 负责提供当前会话上下文
- 由 `TaskGraphExecutor` 只消费这些参数，不自行推断 chat 状态
- 增加“图执行占用 chat 写入权”的单一状态源，避免 VM 和执行器双重判断

---

## 4. 其他风险判断

### 4.1 复用 `SubagentActivityCardControl` 的前提不足

当前右侧 `SubagentActivities` 是从 `/session/{id}/children` 拉取的。

如果某个策略不产生 child session，而只是复用当前 session 发消息，则它并不会自然出现在现有子会话活动流里。

**建议**

- `ChildSession` 可以复用现有 `SubagentActivityCardControl`
- `InSession...` 路径单独做节点内状态展示，不直接复用“子会话卡片”概念

### 4.2 `Self-Mutation` 首版复杂度偏高

当前执行器已有受控的动态扩展能力，但原方案中的 `Self-Mutation` 已升级到:

- 增删节点
- 改依赖
- 自动应用
- 撤销窗口
- 动画高亮

这对首版来说过重，涉及:

- DAG 校验
- 正在运行节点的合法性
- UI 回滚
- 执行游标重建

**建议**

- v3.0 只保留受控扩图
- 删除节点、改依赖、自动应用确认放到 v3.1

### 4.3 “禁用 composer”只能解决 UI 层问题

禁用输入框可以减少用户干扰，但不能代替真实的执行串行化。

**建议**

- 在会话执行层增加串行占用状态
- 避免其他入口在图执行期间继续向同一 session 写入

---

## 5. 建议调整后的实施顺序

### 5.1 建议作为 v3.0 落地的内容

优先实现:

1. `TaskNode.DelegationStrategy`
2. `ChildSession` 支持
3. Chat 内嵌 `TaskGraphLivePanel`
4. 自动模式 / 模板模式入口迁移到 Chat
5. `Inline` 仅用于规划、总结、决策节点

### 5.2 建议降级或延后的内容

建议降级:

- `InSessionSubagent`
  - 首版仅定义为“复用当前会话执行节点”
  - 不宣称其等价于原生子 agent
  - 不直接复用子会话卡片体系

建议延后:

- 图结构自我调节的完整 mutation 机制
- 自动应用 + 撤销
- 多级嵌套 subagent

---

## 6. 推荐的方案修订点

建议直接修改原方案中的以下表述:

### 6.1 策略命名

- 将 `InSessionSubagent` 改为更保守命名
- 或在文档中明确:
  - v3 的 `InSessionSubagent` 是“会话内执行近似模式”
  - 不代表底层已具备原生 task tool 子代理抽象

### 6.2 `ChildSession` 收益描述

- 将“共享父上下文”“节省 token”改为:
  - “可能收益，待验证”
- 将确定性收益改写为:
  - 归属关系清晰
  - 可通过 children 接口观察
  - 比完全顶层散开的 session 更容易组织

### 6.3 执行请求模型

建议在执行器入参中补充:

```csharp
public sealed record TaskGraphExecutionRequest(
    string WorkingDirectory,
    string Permission,
    string Model,
    string? ConversationSessionId,
    bool AllowInlineExecution,
    TaskGraphExecutionPresentationMode PresentationMode);
```

这样可以避免执行器自行推测当前图是否来自 chat。

### 6.4 `Self-Mutation` 缩 scope

建议 v3.0 只支持:

- 在特定 `Plan` 节点后追加新节点
- 不支持删除节点
- 不支持修改已存在边
- 不支持自动应用确认弹窗

---

## 7. 最终判断

这份方案的核心方向值得保留，但应先修订再执行。

**推荐结论**

- 保留整体路线
- 修正委托策略语义
- 压缩首版范围
- 将 `ChildSession` 的真实语义从“共享上下文”调整为“父子归属的独立 session”
- 将 `InSessionSubagent` 的真实语义从“原生子 agent”调整为“复用当前会话执行节点的近似模式”

如果按上述方式收口，v3 会更容易真正落地，而且不会把后续架构绑死在一个过强假设上。

---

## 8. 后续建议

建议下一步产出一份修订版方案，或直接在原方案基础上更新以下章节:

- §2.1 OpenCode 原生能力
- §2.2 omo 借鉴结论
- §3 委托策略模型
- §4.2 自动模式执行流
- §4.3 Bug 列表模板对 `ChildSession` 的解释
- §8 风险与缓解

也可以进一步补一份“v3.0 实施最小集”文档，作为真正开发的执行依据。
