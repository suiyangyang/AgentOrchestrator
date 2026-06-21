# TaskGraph 委托策略与对话内编排修订方案

> 状态: 修订方案，待确认后实施
> 日期: 2026-06-21
> 适用范围: AgentOrchestrator TaskGraph v3

---

## 1. 背景

当前 TaskGraph 已具备以下基础能力:

- 图结构建模、编辑与持久化
- 多种图生成入口
- 节点执行、失败重试、继续执行
- 运行期有限扩图
- 独立 TaskGraph 工作区中的实时状态展示

当前实现的核心限制也很明确:

- 节点执行路径固定为“每节点新建一个顶层 session”
- TaskGraph 主要运行在独立工作区，未进入 Chat 主工作流
- 节点无法表达不同的委托方式
- Chat 中虽然已有子会话活动展示，但尚未与 TaskGraph 执行统一

本方案的目标，是在不推翻现有 v1/v2 能力的前提下，为 TaskGraph 增加:

1. 节点级委托策略
2. Chat 内嵌的 TaskGraph 生成与执行体验
3. 与现有 OpenCode 能力一致的执行语义

---

## 2. 目标

### 2.1 核心目标

v3 只解决两件事:

1. **节点级委托策略**
   - 每个节点显式记录自己的执行方式
   - 执行器根据策略走不同路径
   - 保持默认行为向后兼容

2. **对话内 TaskGraph**
   - 用户可在 Chat 中直接触发自动编排或模板编排
   - 执行进度在 Chat 中实时可见
   - 执行结果回流 Chat，而不是要求用户切换工作区

### 2.2 非目标

本阶段明确不做:

- 多图并发执行
- 多级嵌套代理树
- 跨机器暂停/迁移/恢复
- 完整的图结构自我重写系统
- 基于历史学习的自动策略优化
- OpenCode plugin 集成

---

## 3. 设计原则

### 3.1 与实际能力对齐

方案中的执行语义必须与当前项目和 OpenCode 已确认能力一致，不引入“文档里存在、代码里不存在”的抽象。

### 3.2 向后兼容优先

旧图在不修改数据的情况下应保持原有执行行为，即默认继续按 `NewSession` 执行。

### 3.3 Chat 是主工作流，TaskGraph 工作区继续保留

Chat 内嵌 TaskGraph 是默认体验；独立 TaskGraph 工作区继续保留，用于高级编辑、离线规划和诊断。

### 3.4 首版只做稳定可解释的能力

v3 首版优先做:

- 明确策略
- 稳定执行
- 清晰状态显示

高不确定性能力延后。

---

## 4. 当前能力基线

基于当前代码和 SDK，可确认的事实如下。

### 4.1 OpenCode 已确认能力

- 支持创建 session
- `SessionCreateRequest` 支持 `ParentID`
- `Session` 模型包含 `ParentID`
- 支持获取某个 session 的 children
- 支持发送消息并获取流式输出

### 4.2 当前项目已使用能力

- `IAgentGateway.CreateSessionAsync(...)`
- `IAgentGateway.SendMessageAsync(...)`
- `OpenCodeAgentGateway.GetSubagentActivitiesAsync(...)` 已使用 children 接口
- `TaskGraphExecutor` 当前固定为每节点创建新 session 再执行

### 4.3 当前项目未建模的能力

当前项目尚未在 `IAgentGateway` 层清晰建模以下语义:

- 创建 child session
- 在现有 chat session 内执行一个 TaskGraph 节点
- 用统一方式区分“独立子会话执行”和“同会话内执行”

因此 v3 设计必须先把这些语义建模清楚，再落代码。

---

## 5. v3 范围

### 5.1 在范围内

- `TaskNode` 增加委托策略字段并持久化
- `TaskGraph` 增加来源与会话上下文字段
- `IAgentGateway` 增加 child session 能力
- `TaskGraphExecutor` 按策略执行
- Chat 内新增 TaskGraph 实时面板
- Chat 中新增自动编排与模板编排入口
- Bug 列表模板接入 Chat
- 对现有动态扩图能力做受控增强

### 5.2 不在范围内

- 将“同 session 执行”包装成真正原生子代理协议
- 节点运行期间任意切换执行策略
- 自动删除节点、重写全图结构
- 在右侧侧栏构建完整的多层子图浏览器

---

## 6. 委托策略模型

### 6.1 策略枚举

v3 采用以下 4 种策略:

```csharp
public enum TaskNodeDelegationStrategy
{
    NewSession = 0,
    ChildSession = 1,
    InSessionExecution = 2,
    Inline = 3,
}
```

### 6.2 语义定义

#### 6.2.1 `NewSession`

含义:

- 为当前节点创建一个新的顶层 session
- 与父 Chat 或其它节点 session 没有直接父子关系

适用:

- 文件修改类任务
- 需要独立重试的任务
- 并行潜力高的任务
- 需要强隔离的长链路执行

这是默认策略，也是当前行为。

#### 6.2.2 `ChildSession`

含义:

- 为当前节点创建一个带 `ParentID` 的独立 session
- 该 session 与父 session 存在可追踪的父子关系

适用:

- 希望把一批节点组织在某个父任务之下
- 希望复用现有 children 观察能力
- 希望在 UI 中稳定展示节点与父会话的归属

注意:

- `ChildSession` 的确定性收益是**归属关系清晰**
- 是否自动继承上下文、工具、权限、以及是否节省 token，均视为**待验证行为**
- 因此本方案不把“共享上下文”作为 `ChildSession` 的前置语义

#### 6.2.3 `InSessionExecution`

含义:

- 不创建新 session
- 直接在当前 Chat 对应的 session 中执行该节点
- 属于“复用当前会话执行节点”的近似模式

适用:

- 轻量分析
- 与当前对话强关联的短任务
- 需要结果自然进入当前会话语境的节点

注意:

- 本策略不等价于“原生子代理”
- 它不会天然产生独立 child session
- 不应直接复用“子会话活动”这套语义

#### 6.2.4 `Inline`

含义:

- 不创建新 session
- 不将其作为独立执行代理节点处理
- 直接把该节点作为当前编排流中的轻量推理步骤

适用:

- 规划
- 总结
- 决策
- 人工输入前的说明节点

限制:

- `Inline` 不用于文件修改类节点
- 不用于长工具链任务

---

## 7. 策略选择规则

### 7.1 默认规则

系统默认采用规则选择，允许高级用户覆写。

建议规则如下:

```csharp
public static TaskNodeDelegationStrategy SuggestStrategy(TaskNode node, TaskGraph graph)
{
    if (node.Kind is TaskNodeKind.Plan or TaskNodeKind.Decision or TaskNodeKind.HumanInput)
    {
        return TaskNodeDelegationStrategy.Inline;
    }

    if (graph.OriginHint is TaskGraphOriginHint.ChatAuto or TaskGraphOriginHint.ChatTemplate)
    {
        if (node.Kind == TaskNodeKind.Execute && node.Tags.Contains("Lightweight"))
        {
            return TaskNodeDelegationStrategy.InSessionExecution;
        }
    }

    if (node.Tags.Contains("GroupUnderParent"))
    {
        return TaskNodeDelegationStrategy.ChildSession;
    }

    return TaskNodeDelegationStrategy.NewSession;
}
```

### 7.2 人工覆写

允许高级用户在图编辑阶段手动修改策略，但运行中不可修改。

### 7.3 运行时约束

- `NewSession` 可升级为 `ChildSession`
- `ChildSession` 可回退为 `NewSession`
- 运行中的节点不可切换策略
- `InSessionExecution` 与 `Inline` 在运行时不允许切换为 session 型策略

---

## 8. 数据模型

### 8.1 `TaskNode` 扩展

```csharp
[ObservableProperty]
private TaskNodeDelegationStrategy _delegationStrategy = TaskNodeDelegationStrategy.NewSession;

[ObservableProperty]
private string? _parentSessionId;

[ObservableProperty]
private bool _mutationEnabled;
```

字段说明:

- `DelegationStrategy`: 节点执行方式
- `ParentSessionId`: 仅在 `ChildSession` 运行时记录父 session
- `MutationEnabled`: 是否允许该节点触发受控扩图

### 8.2 `TaskGraph` 扩展

```csharp
[ObservableProperty]
private TaskGraphOriginHint _originHint = TaskGraphOriginHint.WorkspaceDirect;

[ObservableProperty]
private string? _conversationSessionId;
```

字段说明:

- `OriginHint`: 图来源
- `ConversationSessionId`: 当图来自 Chat 且允许会话内执行时，记录当前 Chat session

### 8.3 图来源枚举

```csharp
public enum TaskGraphOriginHint
{
    WorkspaceDirect = 0,
    ChatAuto = 1,
    ChatTemplate = 2,
    ChatMention = 3,
}
```

---

## 9. 执行请求模型

为避免执行器自行推测图来自哪里，v3 需要扩展执行请求。

```csharp
public sealed record TaskGraphExecutionRequest(
    string WorkingDirectory,
    string Permission,
    string Model,
    string? ConversationSessionId,
    bool AllowInlineExecution,
    TaskGraphExecutionPresentationMode PresentationMode);
```

配套枚举:

```csharp
public enum TaskGraphExecutionPresentationMode
{
    Workspace = 0,
    ChatEmbedded = 1,
}
```

规则:

- `ChatWorkspaceViewModel` 负责提供 `ConversationSessionId`
- `TaskGraphExecutor` 只消费这些参数
- 执行器不自行判断“当前是否在 Chat”

---

## 10. `IAgentGateway` 扩展

### 10.1 新增接口

```csharp
public interface IAgentGateway
{
    Task<string> CreateSessionAsync(
        SessionCreateRequest request,
        CancellationToken ct = default);

    Task<string> CreateChildSessionAsync(
        string parentSessionId,
        SessionCreateRequest request,
        CancellationToken ct = default);

    Task<IReadOnlyList<RemoteSessionInfo>> ListChildSessionsAsync(
        string parentSessionId,
        CancellationToken ct = default);

    IAsyncEnumerable<ChatStreamChunk> SendMessageAsync(
        string agentSessionId,
        ChatRequest request,
        CancellationToken ct = default);
}
```

### 10.2 设计说明

- `CreateChildSessionAsync(...)` 是 v3 必需能力
- `InSessionExecution` 不新增独立 gateway 接口，复用现有 `SendMessageAsync(...)`
- 不在本阶段引入“原生 subagent tool 调用”抽象

---

## 11. 执行器改造

### 11.1 核心要求

`TaskGraphExecutor` 必须从“固定新建 session”改为“按节点策略选择执行路径”。

### 11.2 执行路径

#### 11.2.1 `NewSession`

执行流程:

1. 调 `CreateSessionAsync(...)`
2. 记录 `node.AgentSessionId`
3. 构造 prompt
4. 调 `SendMessageAsync(...)`
5. 拉取消息并填充节点输出

#### 11.2.2 `ChildSession`

执行流程:

1. 获取父 session id
2. 调 `CreateChildSessionAsync(parentId, ...)`
3. 记录 `node.ParentSessionId` 与 `node.AgentSessionId`
4. 执行消息发送与结果回收

父 session 的选择规则:

- Bug 列表模板中使用图级父 session
- 非模板场景下优先使用图的 `ConversationSessionId`
- 如果当前图无会话上下文，则退回 `NewSession`

#### 11.2.3 `InSessionExecution`

执行流程:

1. 检查 `ConversationSessionId`
2. 若为空，回退到 `NewSession`
3. 使用当前 chat session 直接发送节点 prompt
4. 节点输出只进入 TaskGraph 面板缓冲区
5. 节点完成后生成摘要回填图状态

关键约束:

- 执行期间必须占用 Chat 写入权
- 普通用户消息不能与节点执行并发写入同一 session

#### 11.2.4 `Inline`

执行流程:

1. 检查 `ConversationSessionId` 与 `AllowInlineExecution`
2. 将节点视为轻量编排步骤
3. 在当前图执行流内完成一次轻量推理
4. 结果只写节点输出与状态，不展开为独立子会话

若当前图不在 Chat 中执行，则 `Inline` 回退为 `NewSession`。

### 11.3 失败与回退规则

- `ChildSession` 创建失败时回退 `NewSession`
- `InSessionExecution` 无 `ConversationSessionId` 时回退 `NewSession`
- `Inline` 不满足执行条件时回退 `NewSession`

这样可以保证旧执行器行为仍然可达。

---

## 12. Chat 内嵌 TaskGraph

### 12.1 入口

Chat 中新增两个入口:

- `智能编排`
- `模板编排`

可选保留 `/plan` 命令作为快捷方式。

### 12.2 用户流程

#### 自动模式

1. 用户在 Chat 中输入目标或点击 `智能编排`
2. 系统基于用户意图生成 TaskGraph
3. 图挂载到 Chat 顶部实时面板
4. 执行器开始运行
5. 用户在 Chat 中看到节点进度
6. 执行结束后生成摘要消息插入 Chat

#### 模板模式

1. 用户点击 `模板编排`
2. 选择模板
3. 填写模板输入
4. 生成 TaskGraph
5. 图在 Chat 中执行并可视化展示

### 12.3 会话写入权

这是 Chat 内嵌方案的关键约束。

新增单一状态源:

- `ChatExecutionLease`

含义:

- 当图以 `InSessionExecution` 或 `Inline` 执行时，图执行器临时占用当前 Chat session 的写入权
- 占用期间:
  - composer 禁用
  - 普通发消息入口禁用
  - 其它自动发送路径不可写入同一 session

释放时机:

- 图执行完成
- 图执行取消
- 图执行失败并退出会话内路径

---

## 13. 实时面板设计

### 13.1 位置

挂在 Chat 头部下方。

### 13.2 内容

面板显示:

- 图标题
- 总体状态
- 完成数 / 失败数 / 运行中数
- 节点列表或简图
- 当前运行节点
- 最近日志

### 13.3 节点展示

每个节点展示:

- 标题
- 状态
- 策略徽章
- token / 工具 / 最近更新时间

策略徽章说明:

- `NewSession`
- `ChildSession`
- `InSessionExecution`
- `Inline`

### 13.4 与右侧子会话活动区的关系

- `ChildSession` 节点可以接入现有子会话活动展示
- `InSessionExecution` 不复用子会话卡片
- 右侧侧栏只展示真实可枚举的子 session

---

## 14. 模板模式

### 14.1 首批模板

v3 保留并收敛到这三类:

- 任务列表
- 复杂功能开发
- Bug 列表

### 14.2 Bug 列表模板

Bug 列表模板是 v3 的重点场景。

建议图结构:

```text
[n0: parse_bug_list]      Plan / Inline
    ↓
[n1..nN: analyze_bug_i]   Execute / ChildSession
    ↓
[m1..mN: fix_bug_i]       Execute / ChildSession
    ↓
[bug_report]             Plan / Inline
```

设计解释:

- `parse_bug_list` 负责把原始输入解析成结构化 bug 列表
- 每个 bug 生成分析节点和修复节点
- `ChildSession` 的作用是将同一批 bug 节点组织在统一父任务之下
- 不把“共享上下文”和“节省 token”作为首版前提

如果后续验证确认 `ParentID` 能稳定继承有价值上下文，再在 v3.1 增加相关优化。

---

## 15. 动态扩图

### 15.1 v3 只做受控扩图

保留并增强现有受控扩图能力，但不实现“任意自我改图”。

### 15.2 v3 支持的扩图范围

仅支持:

- 在特定 `Plan` 节点后追加新节点
- 为新节点添加依赖
- 重新拓扑排序

不支持:

- 删除已有节点
- 修改既有边
- 自动替换运行中节点
- 自动确认弹窗 + 撤销窗口

### 15.3 接口建议

```csharp
public interface ITaskGraphMutationApplier
{
    Task<MutationApplyResult> TryAppendNodesAsync(
        TaskGraph graph,
        TaskNode node,
        NodeExecutionResult result,
        CancellationToken ct);
}
```

这能保持能力收敛，避免首版把执行器复杂度推得过高。

---

## 16. 运行时事件

`TaskGraphRuntimeHub` 扩展为以下事件集合:

```csharp
public interface ITaskGraphRuntimeHub
{
    event EventHandler<TaskGraphChunkEventArgs>? ChunkReceived;
    event EventHandler<TaskGraphNodeEventArgs>? NodeChanged;
    event EventHandler<TaskGraphStrategyEventArgs>? StrategyResolved;
    event EventHandler<TaskGraphExecutionEventArgs>? ExecutionStateChanged;
    event EventHandler<TaskGraphMutationEventArgs>? GraphExpanded;
}
```

用途:

- `ChunkReceived`: 节点流式输出
- `NodeChanged`: 节点状态更新
- `StrategyResolved`: 节点策略确定
- `ExecutionStateChanged`: 图级状态变化
- `GraphExpanded`: 受控扩图完成

---

## 17. 架构改造点

### 17.1 UI

- `ChatWorkspaceControl` 增加实时 TaskGraph 面板区域
- 保留 `TaskGraphWorkspaceControl`
- 新增模板输入对话框

### 17.2 ViewModel

- `ChatWorkspaceViewModel`
  - 新增 `ActiveGraph`
  - 新增 `LivePanel`
  - 管理 Chat 会话写入权
  - 触发自动编排与模板编排

- `TaskGraphLivePanelViewModel`
  - 订阅 runtime 事件
  - 维护节点实时状态

### 17.3 Services

- `TaskGraphPlanner`
  - 写入 `OriginHint`
  - 为节点建议策略

- `TaskGraphExecutor`
  - 策略分流执行
  - 处理回退逻辑

- `OpenCodeAgentGateway`
  - 实现 child session 能力

---

## 18. 风险与缓解

### 18.1 `ParentID` 继承语义不明确

风险:

- child session 不一定继承有价值上下文

缓解:

- 将其视为待验证能力
- 首版不把该假设写进执行正确性

### 18.2 同 session 执行与普通 Chat 消息竞争

风险:

- 图节点与用户消息并发写入同一 session

缓解:

- 引入 `ChatExecutionLease`
- 执行期间统一禁用其它写入路径

### 18.3 首版动态扩图过重

风险:

- DAG 合法性、UI 回滚和执行游标复杂度过高

缓解:

- v3 只做追加节点式扩图

### 18.4 面板展示与真实会话语义混淆

风险:

- 把同会话节点误展示为“子会话”

缓解:

- 只对真实 child session 使用子会话活动视图

---

## 19. 实施顺序

### 阶段 1: 模型与执行基座

1. 扩展 `TaskNode`
2. 扩展 `TaskGraph`
3. 扩展 `TaskGraphExecutionRequest`
4. 扩展 `IAgentGateway`
5. 实现 `CreateChildSessionAsync(...)`

### 阶段 2: 执行器分流

1. 为执行器增加策略路由
2. 增加失败回退逻辑
3. 增加运行时事件

### 阶段 3: Chat 集成

1. `ChatWorkspaceViewModel` 增加图入口
2. 实现 Chat 实时面板
3. 增加会话写入权控制

### 阶段 4: 模板接入

1. 接入任务列表模板
2. 接入复杂功能模板
3. 接入 Bug 列表模板

### 阶段 5: 受控扩图

1. 保留现有扩图能力
2. 升级为统一追加节点接口

---

## 20. 兼容性

### 20.1 与旧图兼容

- 旧图未包含新字段时，`DelegationStrategy` 默认为 `NewSession`
- 原有行为不变

### 20.2 与 v2 执行 API 兼容

- `ExecuteAsync`
- `RetryFailedAsync`
- `ContinueAsync`
- `ReconcileAsync`

这些入口保持不变，只调整内部实现。

### 20.3 与 Chat 兼容

- 普通对话流程保持不变
- 仅在图执行期间启用写入占用逻辑

---

## 21. 最终决策

v3 采用以下落地方向:

1. 保留四种策略，但将第三种正式定义为 `InSessionExecution`
2. `ChildSession` 的确定语义为“父子归属的独立 session”
3. Chat 成为默认编排入口
4. 独立 TaskGraph 工作区继续保留
5. 动态扩图在 v3 仅支持受控追加

这一路线可以在不高估底层能力的前提下，把 TaskGraph 真正带入 Chat 主工作流，同时保持现有实现可演进、可验证、可回退。

