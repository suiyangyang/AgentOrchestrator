# TaskGraph 委托策略与对话内编排 方案 (v3)

> 状态: 方案已锁定,待执行
> 决策日期: 2026-06-21
> 范围: 在 v1 画图 + v2 编排执行底座之上,叠加
> (a) **节点级委托策略** —— 每节点显式选择 new session / in-session subagent;
> (b) **对话内 TaskGraph 体验** —— 项目下对话中自动/模板生成 TaskGraph,实时看到执行进度
> 配套文档:
> - [`TaskGraph-方案.md`](./TaskGraph-方案.md) (v1, 已锁定):画图工具 + 节点编辑 + 持久化
> - [`TaskGraph-改造计划.md`](./TaskGraph-改造计划.md) (v1, 已锁定)
> - [`TaskGraph-编排方案.md`](./TaskGraph-编排方案.md) (v2, 已锁定):三种输入模式 + 节点执行 + 弹窗
> - [`TaskGraph-委托与对话内编排-改造计划.md`](./TaskGraph-委托与对话内编排-改造计划.md) (v3, 实施计划)

---

## 0. 阅读指引

| 想了解 | 读哪份 |
|---|---|
| v1 画图基础 | `TaskGraph-方案.md` |
| v2 三模式输入 + 节点执行 | `TaskGraph-编排方案.md` |
| **v3 节点委托策略 + 对话内编排** | **本文** |
| 怎么落地 v3 | `TaskGraph-委托与对话内编排-改造计划.md` |

---

## 1. 目标与范围

### 1.1 目标

1. **节点级委托策略**:TaskGraph 中每个节点可显式标注 `DelegationStrategy`,在执行时按策略委托给:
   - `NewSession`(当前默认,新增 session)
   - `InSessionSubagent`(OpenCode 原生 task/子 agent 机制,共享父 session 上下文)
   - `Inline`(在当前 chat session 内作为消息追加,不创建新 agent session,只读 LLM)
2. **对话内 TaskGraph**:
   - **自动模式**:在项目下对话中,用户说一句意图,后端按需动态生成 TaskGraph 并执行;**运行中允许**根据 LLM 反馈**自我调节** TaskGraph(增删节点、改依赖)
   - **模板模式**:用户在项目下对话中选模板(任务列表 / Bug 列表 / 复杂功能开发),填入批量数据(如 bug list),动态生成 TaskGraph
   - **实时 UI**:当前 chat session 中实时看到 TaskGraph 进度(节点状态/边动画/子会话卡片),不需要切到"任务编排"工作区

### 1.2 v3 范围

| 在范围内 | 范围外(后续) |
|---|---|
| `TaskNode.DelegationStrategy` 枚举 + 持久化 | 节点级用户 UI 提示气泡(暂不做,默认 AI 选) |
| `IAgentGateway` 增加 `CreateChildSessionAsync` / `DelegateSubagentAsync` | 多级嵌套 subagent(>2 级) |
| `TaskGraphExecutor` 按 strategy 选路径 | TaskGraph 自学习策略(从历史回放学习) |
| Chat 内嵌 TaskGraph 实时面板(挂在 chat 右侧或消息流上方) | 侧边栏展示子 TaskGraph(暂用现有 session 列表) |
| 自动模式:对话触发 → LLM 决策 → 生成图 → 执行 → 自我调节 | 多图并发(同一对话多图) |
| 模板模式:模板选择 + 批量数据(bug list)→ 生成 | 模板可视化编辑 |
| 实时 UI:节点状态/进度/子会话卡片/边动画 | 全功能画布缩放/平移(沿用 v1 已有 zoom) |

### 1.3 显式不做(v3)

- 节点级策略由 LLM 自动选择(默认);UI 上提供"切换策略"下拉供高级用户
- 不实现 TaskGraph "暂停-迁移-恢复" 到另一台机器
- 不做"自动调节图"的可视化编辑器(只允许 AI 改动,不暴露 UI 按钮)
- 不引入 OpenCode 的 plugin 机制(本方案 v3 不做)

---

## 2. 关键事实与基线(基于实际代码 + OpenCode SDK 现状)

> 本节是设计的事实基础,所有架构决策都建立在此之上。

### 2.1 OpenCode 原生能力(SDK 已暴露)

| 能力 | OpenCode SDK 暴露 | 现状(本项目是否用) |
|---|---|---|
| Session 创建 | `POST /session` | **使用**:已通过 `IAgentGateway.CreateSessionAsync` |
| **Child Session(ParentID)** | `Session.ParentID` 字段 + `SessionCreateRequest.ParentID` | **未使用**:`IAgentGateway` 接口未暴露 `ParentID`,执行器目前总是创建顶层 session |
| Session Fork(从某条 message 分叉) | `POST /session/{id}/fork` + `SessionForkRequest` | **未使用** |
| 子会话列表 | `GET /session/{id}/children` | **已使用**:`OpenCodeAgentGateway.GetSubagentActivitiesAsync` 已经在用,生成右侧 Subagent 卡片 |
| Session 消息(Part 含 text/thought/tool/task) | `Part` discriminated union | **已使用**:`OpenCodeAgentGateway` 转换 `task` tool 为 Task 块 |
| `SessionPromptRequest.Agent` 字段(指定 primary/build subagent) | 已暴露 | **未使用**:v2 创建 session 时不传 Agent |
| Session Status(SSE) | `SessionStatusIdle/Retry/Busy` | **已使用** |

**结论**:
- OpenCode 的"子 session"和"fork"能力**SDK 已具备**,但本项目**未启用**
- 要支持"in-session subagent"委托,只需在 `IAgentGateway` 暴露 `CreateChildSessionAsync(parentId, ...)` 即可

### 2.2 omo(OhMyOpenCode)的核心抽象(经设计借鉴)

omo 在 OpenCode 之上的关键设计点:

1. **Subagent 是"指定 Agent 字段"**:omo 的子 agent 通过 `SessionPromptRequest.Agent` 字段切换 primary/build plan/build subagent,**不创建新 session**,而是同一个 session 中切角色
2. **6 段式委托 Prompt**:`TASK / EXPECTED OUTCOME / REQUIRED TOOLS / MUST DO / MUST NOT DO / CONTEXT` —— 借鉴用于 TaskGraph 节点的 `TaskNode.DelegationPrompt`
3. **Category + Skill 系统**:
   - Category 决定**模型和工具集**(如 `visual-engineering` 用 Sonnet + screenshot tools)
   - Skill 决定**附加指令**(如 `frontend-ui-ux` 注入 UI 约束)
4. **Delegation Prompt 通过 task() tool 触发**:omo 的"主 agent"通过 `task` 工具调用子 agent,子 agent 完成后 summary 回到主 agent 上下文

**对 v3 的启示**:
- `NewSession` 模式 = 当前实现(sessionId 新建)
- `InSessionSubagent` 模式 = **同一个 sessionId**,但用 OpenCode 的 `task` tool 触发,父 session 持有上下文
- `Inline` 模式 = **不创建 session**,直接把节点 prompt 写到当前 chat 流(纯消息,无 tool)

### 2.3 当前代码基线

| 文件 | 状态 | v3 影响 |
|---|---|---|
| `Models/TaskGraph/TaskNode.cs` | 171 行,无 `DelegationStrategy` | **扩展**:加 `DelegationStrategy` 枚举字段 + `ParentSessionId`(subagent 时存) |
| `Services/Agent/IAgentGateway.cs` | 112 行,无 child session | **扩展**:加 `CreateChildSessionAsync` / `DelegateAsSubagentAsync` |
| `Services/Agent/OpenCodeAgentGateway.cs` | 已用 children endpoint | **扩展**:在 `CreateSessionAsync` 支持可选 `ParentID` |
| `Services/TaskGraph/TaskGraphExecutor.cs` | 402 行,每节点 new session | **重写**:`ExecuteNodeAsync` 按 strategy 选路径 |
| `Services/TaskGraph/TaskGraphRuntimeHub.cs` | 16 行,2 个事件 | **扩展**:加 `StrategyResolved` 事件,加 `SelfMutated` 事件(节点自调节) |
| `ViewModels/TaskGraphWorkspaceViewModel.cs` | 1493 行,4 输入模式已实现 | **复用**:4 输入模式全部沿用 |
| `ViewModels/ChatWorkspaceViewModel.cs` | 1530 行,有 `SubagentActivities` 集合 | **扩展**:加 `ActiveGraph` 引用 + 实时面板 VM |
| `ViewModels/SubagentActivityViewModel.cs` | 75 行,单 subagent 卡片 | **复用**:在 chat 内嵌 TaskGraph 中,每个 InSessionSubagent 节点渲染一张 subagent 卡片 |
| `Controls/SubagentActivityCardControl.axaml` | 已存在 | **复用** |
| `Controls/TaskGraphWorkspaceControl.axaml` | 已存在(独立工作区) | **沿用**(任务编排 tab 不移除,作为可平移模式) |
| `Controls/ChatWorkspaceControl.axaml` | 已存在(chat 主体) | **扩展**:在 chat 头部下方加 `TaskGraphLivePanelControl` |
| `OpenCode.Client/Models/Session.cs` | `Session.ParentID` 已存在 | **复用** |
| `OpenCode.Client/Requests/SessionRequests.cs` | `SessionCreateRequest.ParentID` 已存在 | **复用** |

### 2.4 当前能力 vs v3 目标

| v3 目标 | 当前能力 | 缺口 |
|---|---|---|
| 节点级委托策略 | 无,固定 new session | 加枚举 + 持久化 + 执行路径分支 |
| Chat 内嵌 TaskGraph | 无(只有独立工作区) | 新增 `TaskGraphLivePanelControl` + ChatVM 引用图 |
| 自动模式 | v2 有 `GenerateFromIntentAsync`,但只在 TaskGraph 工作区触发 | 把意图输入 + 图生成移到 chat 流中 |
| 模板模式 | v2 有 `CreateTemplateGraphAsync`,但需切到工作区 | 把"模板选择"挂载到 chat 工具栏 + bug list 输入面板 |
| 实时 UI | v2 TaskGraph 工作区有实时状态 | 复用 `TaskGraphRuntimeHub` 事件流,在 chat 内嵌区域订阅 |
| 自我调节 | `TaskGraphDynamicExpander` 已支持(运行期扩图) | 升级为通用"自调节"接口,允许 AI 在 run 中改图 |

---

## 3. 委托策略模型 (DelegationStrategy)

### 3.1 枚举

```csharp
public enum TaskNodeDelegationStrategy
{
    /// <summary>
    /// 默认。新建顶层 Agent Session,与父 session 上下文完全隔离。
    /// 适合:独立子任务、长链路、需要并行、可能需要 retry。
    /// 对应 OpenCode:POST /session (不传 ParentID)
    /// </summary>
    NewSession = 0,

    /// <summary>
    /// 在指定父 Session 内创建子 session(共享父项目,独立上下文)。
    /// 适合:与父任务有上下文关联(同项目、同主题),但仍需独立可重试。
    /// 对应 OpenCode:POST /session (ParentID=parent)
    /// 父 session 可通过 GET /session/{parent}/children 观察该子 session。
    /// </summary>
    ChildSession = 1,

    /// <summary>
    /// 在当前 chat session 内调用 LLM 作为 subagent(共享父 session 完整上下文)。
    /// 适合:轻量任务、对话流内自然延伸、需要父 session 看到完整推理。
    /// 对应 OpenCode:同一 sessionId,Prompt 改写为"作为 subagent 执行 X"
    /// (实际实现走 LLM tool-use 的 task 工具;v3 简化为直接 SendMessage)
    /// </summary>
    InSessionSubagent = 2,

    /// <summary>
    /// 不创建任何 agent session,直接把节点 prompt 注入当前 chat 流,
    /// 等待 LLM 在当前流内"思考"(不产生可执行的子会话)。
    /// 适合:规划/决策/总结类节点(无副作用)。
    /// 对应 OpenCode:同一 sessionId,提示词是"分析以下并给出结论",不调工具。
    /// </summary>
    Inline = 3,
}
```

### 3.2 决策矩阵(每策略适用场景)

| 信号 | NewSession | ChildSession | InSessionSubagent | Inline |
|---|---|---|---|---|
| 涉及文件修改 | ✅ 强匹配 | ✅ | ❌(共享上下文易污染) | ❌ |
| 需要 retry 3+ 次 | ✅ | ✅ | ⚠(污染父) | ❌ |
| 需要并行 | ✅(天然) | ✅(同父并行) | ⚠(单 session 不能并行) | ❌ |
| 上下文长度 | 每个独立(优) | 子独立(优) | **共享**(可能爆) | 共享(可能爆) |
| 父 session 需要看到子推理 | ❌(隔离) | ⚠(可拉 children) | ✅(同流) | ✅ |
| 副作用隔离 | ✅ 强 | ✅ | ❌ | N/A |
| 决策/规划/总结 | ❌(浪费) | ❌ | ✅(轻量) | ✅(最佳) |
| 多步推理(需工具) | ✅ | ✅ | ✅ | ❌ |
| 适合模板生成的 bug list 项 | ✅(每项独立) | ⚠(共享 bug list 上下文) | ❌ | ❌ |

### 3.3 自动选择规则(默认 AI 决策,UI 可改)

`TaskGraphPlanner` 在生成图时,为每个节点写入 `DelegationStrategy`。决策函数:

```csharp
public static TaskNodeDelegationStrategy SuggestStrategy(TaskNode node, TaskGraph graph)
{
    // Rule 1: 无副作用节点(Kind != Execute && Kind != Verify) → Inline
    if (node.Kind is TaskNodeKind.Plan or TaskNodeKind.Decision or TaskNodeKind.HumanInput)
        return TaskNodeDelegationStrategy.Inline;

    // Rule 2: 长链路(>= 5 个 Execute 节点)中的下游节点 → ChildSession(共享父项目,降本)
    var executableAncestors = CountExecutableAncestors(graph, node);
    if (executableAncestors >= 5)
        return TaskNodeDelegationStrategy.ChildSession;

    // Rule 3: 节点 prompt 显式引用"父任务的输出"作为唯一上下文来源 → ChildSession
    if (node.Prompt.Contains("based on the previous") || node.Prompt.Contains("根据上文"))
        return TaskNodeDelegationStrategy.ChildSession;

    // Rule 4: 用户在 chat 中说"我希望你直接在对话里回复" → InSessionSubagent
    // (信号来自 ChatWorkspaceViewModel,在生成图时传入)
    if (graph.OriginHint == TaskGraphOriginHint.InChatConversational)
        return TaskNodeDelegationStrategy.InSessionSubagent;

    // Rule 5: 默认
    return TaskNodeDelegationStrategy.NewSession;
}
```

`TaskGraphOriginHint` 是个新枚举,标识图是怎么来的(影响默认策略):

```csharp
public enum TaskGraphOriginHint
{
    /// <summary>从 TaskGraph 工作区直接生成(传统路径)</summary>
    WorkspaceDirect = 0,

    /// <summary>在 chat 中通过自动模式生成(LLM 决策)</summary>
    ChatAuto = 1,

    /// <summary>在 chat 中通过模板生成(bug list 等)</summary>
    ChatTemplate = 2,

    /// <summary>在 chat 中用户显式 @task 触发(将来)</summary>
    ChatMention = 3,
}
```

### 3.4 运行时约束

- **不能**在 run 中把 `NewSession` 改成 `Inline` 之后回退(数据已写,无意义)
- **可以**在 run 中把 `ChildSession` 升级为 `NewSession`(发现子 session 隔离不够,需要完全隔离)
- `InSessionSubagent` ↔ `ChildSession` 互转 **禁止**(共享上下文 vs 独立 session 是质变)

---

## 4. 三种模式设计

### 4.1 模式总览

| 模式 | 触发 | 输入 | 输出 | 自我调节 |
|---|---|---|---|---|
| **自动模式** | chat 工具栏 [智能编排] 按钮 / 用户输入 `/plan <text>` / AI 判断用户意图复杂 | 自然语言意图 | TaskGraph(LLM 解析) | ✅ run 中允许 |
| **模板模式** | chat 工具栏 [模板编排] 下拉(任务列表 / 复杂功能开发 / Bug 列表) | 模板类型 + 批量数据(bug list) | TaskGraph(模板构造) | ⚠ 视模板而定(复杂功能开发模板支持,bug list 暂不支持) |
| **实时 UI** | 自动/模板模式触发后自动挂载 | (无) | chat 内嵌面板,实时渲染 | (无,只是显示) |

> **重要**:v3 不在独立 TaskGraph 工作区强制运行,而是**默认在 chat 中挂载**。原 TaskGraph 工作区保留作为"高级编辑 + 离线规划"场景。

### 4.2 自动模式:对话触发 → LLM 决策 → 生成图 → 执行 → 自我调节

#### 4.2.1 入口

| 入口 | 路径 |
|---|---|
| 工具栏按钮 | Chat 顶部工具栏新增 [智能编排] 按钮 |
| Slash 命令 | `/plan 我想做一个 todo app` |
| AI 主动建议 | 当用户消息含"分几步/先...然后...最后..."等强规划信号时,chat 主动建议(可选,默认关) |

#### 4.2.2 流程

```text
用户在 chat 中点 [智能编排] 或输入 /plan <text>
   ↓
ChatWorkspaceViewModel.TriggerAutoTaskGraphAsync(text)
   ↓
1. 暂停当前 chat 流(IsStreaming 状态保留)
2. 调 TaskGraphPlanner.CreateFromIntentAsync(text, workingDir, perm, model)
   - 内部走 LlmPlanner,获取 PlannerSchema(TaskGraph)
3. 在 chat 头部下方挂载 TaskGraphLivePanel
   - panel 订阅 TaskGraphRuntimeHub.ChunkReceived / NodeChanged / StrategyResolved / SelfMutated
4. 调 TaskGraphExecutor.ExecuteAsync(graph, request)
   - 顺序执行节点,每个节点按 DelegationStrategy 选择路径
   - 节点创建 session → 派发 StrategyResolved 事件
   - 流式 chunk → 派发 ChunkReceived 事件
   - 节点完成 → 派发 NodeChanged 事件
5. (自我调节)在 run 中,如果 LLMPlanner 在某个节点 prompt 收到"图结构需要调整"信号:
   - 节点返回特殊结构化输出 {"taskgraph_mutation": {...}}
   - TaskGraphDynamicExpander 解析 mutation
   - 应用到 graph(Nodes/Edges 增删改)
   - 派发 SelfMutated 事件,UI 局部刷新
6. 执行结束
   - panel 标记 Completed/Failed
   - chat 流恢复
   - 最终结果(节点 OutputSummary 集合)以"汇总消息"形式回填到 chat 流
```

#### 4.2.3 自我调节协议 (Graph Self-Mutation)

允许 LLM 在某个节点执行时**返回**对图的修改建议:

```json
{
  "taskgraph_mutation": {
    "add_nodes": [
      {"id": "n8", "title": "补充: 错误处理", "kind": "Execute", "depends_on": ["n3"]}
    ],
    "remove_nodes": ["n5"],
    "add_edges": [{"from": "n1", "to": "n8"}],
    "reason": "需求文档未提及错误处理,补充。"
  }
}
```

`TaskGraphDynamicExpander` 扩展为 `TaskGraphMutationApplier`:

```csharp
public interface ITaskGraphMutationApplier
{
    /// <summary>尝试从节点输出中解析 mutation,应用并返回应用结果。</summary>
    Task<MutationApplyResult> TryApplyAsync(
        TaskGraph graph,
        TaskNode node,
        NodeExecutionResult result,
        CancellationToken ct);
}

public sealed record MutationApplyResult(
    bool Applied,
    int NodesAdded,
    int NodesRemoved,
    int EdgesAdded,
    string? Reason);
```

**触发条件**(默认 AI 决策):
- 节点 `Kind == Plan` 时,执行完成后自动尝试解析 mutation
- 节点 prompt 含 `[MUTATION_ENABLED]` 标记时启用
- 自我调节**最多触发 1 次** per node(避免无限递归)

#### 4.2.4 自我调节的可视化

chat 内嵌 panel 收到 `SelfMutated` 事件时:
- 弹一个浮层:"AI 建议调整图结构: 新增 1 节点,删除 1 节点。理由: ... [应用] [拒绝]"
- 默认 5s 后自动应用(auto-apply),可在 chat 设置中关闭
- 应用后 panel 用 highlight 动画标记变更的节点

### 4.3 模板模式:模板选择 + 批量数据 → 生成图

#### 4.3.1 入口

Chat 工具栏 [模板编排] 下拉:
- 任务列表(纯文本列表,无依赖)
- 复杂功能开发(项目文档 + 模板构造)
- **Bug 列表(新增 v3 重点)** —— 用户粘贴 bug 列表,batch 化生成

#### 4.3.2 Bug 列表模板(用户重点需求)

**输入面板**(chat 内嵌弹层):

```text
+------------------------------- Bug 列表编排 --------------------------+
| 模板: [Bug 列表 v]                                                |
| 项目目录: [自动:当前项目]                                            |
| 解析模式: (●) 智能解析  ( ) 手动校对                                 |
|                                                                    |
| 原始输入:                                                          |
| ┌──────────────────────────────────────────────────────────────┐   |
| │ 1. 登录页提交后白屏                                            │   |
| │ 2. 注册表单邮箱校验不通过                                       │   |
| │ 3. 用户列表翻页到第 5 页后崩溃                                  │   |
| │ ...                                                          │   |
| └──────────────────────────────────────────────────────────────┘   |
|                                                                    |
| 解析预览: (● 智能解析 实时显示)                                     |
|   [bug 1: 登录白屏] → 节点 "分析:登录白屏"                          |
|   [bug 2: 邮箱校验] → 节点 "分析:邮箱校验"                          |
|   [bug 3: 翻页崩溃] → 节点 "分析:翻页崩溃"                          |
|   ...                                                              |
|                                                                    |
| 编排: 每个 bug → 1 个"分析"节点 + 1 个"修复"节点(共享 bug 上下文)     |
|                                                                    |
| [取消]                                                [生成并执行]  |
+--------------------------------------------------------------------+
```

**生成的图结构**(Bug 列表模板):
```text
[n0: read_bug_list] (Plan, Inline, 解析原始输入为结构化 bug 项)
   ↓
[n1..nN: analyze_bug_i] (Execute, ChildSession, 每个 bug 一个)
   ↓ 共享 ParentSession = n0 产生的"项目背景上下文"
[n2..nN+1: fix_bug_i] (Execute, ChildSession, 依赖对应 analyze)
   ↓
[bug_report] (Plan, Inline, 汇总所有 fix 结果为结构化报告)
```

> 这正好命中用户当前已有的 v2 `TaskGraphBugStructuredParser` —— 复用即可。

**注意**:Bug 列表模式下,所有 analyze/fix 节点的 `DelegationStrategy = ChildSession`,**共享**一个父 session(由 `n0` 准备"项目背景 + bug 列表结构化结果"),而不是每个 bug 一个独立 session(否则每个子 agent 都要重新读项目背景,浪费 token)。

### 4.4 实时 UI 设计

> 详细 UI 见 §5。这里给关键交互流。

#### 4.4.1 Panel 生命周期

```text
chat 启动
  → panel 默认隐藏
  → 用户触发自动模式/模板模式
  → panel 滑入 chat 头部下方(200ms slide-in)
  → 挂载到 chat 流
  → 图执行中:panel 高度自适应(60px 折叠态 / 360px 展开态)
  → 执行完成:panel 保持显示,5min 后可一键折叠
  → 用户发新消息:panel 自动折叠但保留(避免抢屏)
```

#### 4.4.2 Panel 内部布局

```text
┌─ TaskGraph · Todo App 编排 ────────── [⚙] [⊟] ─┐
│ 状态: 3/7 完成  · 1 失败  · 0 跳过              │
│                                                │
│  ●─────────●─────────●                         │
│  │         │         │                          │
│  ●         ◉         ●                         │
│  需求     设计 [运]  编码                       │
│  ✓        ✓        70% · 1.2k tokens           │
│                                                │
│ [⏸ 暂停] [⏹ 取消] [🔍 详情] [📌 固定]         │
└────────────────────────────────────────────────┘
```

`●` 已完成,`◉` 运行中(脉冲动画),`○` 待执行,`✕` 失败,`⊘` 跳过
边的颜色:实线(已就绪)/ 虚线动画(等待上游)/ 灰色(已过期)

#### 4.4.3 节点 hover / click

- hover:节点卡片微高亮 + 边强调
- click:弹出 `NodeDetailPopover`(轻量 popover,不是 modal),内含:
  - 节点 title / status / 当前 tool / token 数
  - 最近 3 行日志(滚动)
  - [打开完整详情] → 复用现有 `TaskGraphNodeDetailWindow`

#### 4.4.4 节点策略徽章

每节点卡片角落显示策略徽章:
- 🆕 NewSession(默认隐藏,因为最常见)
- 🔗 ChildSession(显示)
- 💬 InSessionSubagent(显示,带蓝边框)
- 📝 Inline(显示,带虚线边)

点击徽章 → 弹出策略切换菜单(高级用户)。运行中节点策略不可改(灰显)。

---

## 5. UI 改造点

### 5.1 Chat 工具栏(扩 1 列 → 2 列)

| 现有 | v3 新增 | 位置 |
|---|---|---|
| 附件按钮 / 模型选择 / 权限选择 | + [智能编排] 按钮 | composer 工具栏,Send 按钮左侧 |
| | + [模板编排] 下拉 | [智能编排] 左侧 |

### 5.2 Chat 流头部下方(新增)

`TaskGraphLivePanelControl`(新控件,见 §5.5),默认隐藏,触发后挂载。

### 5.3 Chat 右侧栏(已有,扩展)

现有 `SubagentActivities` 集合(右侧 subagent 卡片)已经存在。v3 复用:
- 当 `DelegationStrategy = InSessionSubagent` 的节点执行时,自动在右侧追加 subagent 卡片
- 点击 subagent 卡片 → 跳转到对应节点的 chat 流嵌入区(轻量定位)

### 5.4 Composer 行为变更

- 触发智能编排后,composer 临时禁用,显示提示:"编排进行中,完成后可继续对话。"
- 编排完成后,自动把"图结果摘要"以一条 assistant 消息插入 chat 流

### 5.5 新增控件清单

| 控件 | 类型 | 位置 | 职责 |
|---|---|---|---|
| `TaskGraphLivePanelControl` | UserControl | Chat 头部下方 | 实时 TaskGraph 渲染,事件驱动 |
| `TaskGraphLiveNodeControl` | TemplatedControl | Panel 内部 | 单节点卡片(状态/进度/策略徽章) |
| `TaskGraphLiveEdgeControl` | Shape(自绘) | Panel 内部 | 边(虚线动画/实线/灰色) |
| `NodeDetailPopover` | Popup | Panel 内 | hover/click 节点时弹出 |
| `StrategyBadgeControl` | TemplatedControl | 节点卡片角落 | 策略徽章 |
| `BugListInputDialog` | Window(模态) | chat 触发模板 | 输入 + 预览 + 生成 |
| `GraphMutationConfirmDialog` | Window(模态) | 自我调节时 | "应用/拒绝" mutation |

### 5.6 样式追加(`App.axaml`)

```xml
<!-- 实时面板容器 -->
<Style Selector="Border.taskgraph-live-panel">
  <Setter Property="Background" Value="#FFFFFF"/>
  <Setter Property="BorderBrush" Value="#E2E5EA"/>
  <Setter Property="BorderThickness" Value="1"/>
  <Setter Property="CornerRadius" Value="8"/>
  <Setter Property="Margin" Value="0,0,0,12"/>
  <Setter Property="Padding" Value="12"/>
</Style>

<!-- 节点卡片 -->
<Style Selector="Border.taskgraph-live-node">
  <Setter Property="Background" Value="#FFFFFF"/>
  <Setter Property="BorderBrush" Value="#E2E5EA"/>
  <Setter Property="BorderThickness" Value="1"/>
  <Setter Property="CornerRadius" Value="6"/>
  <Setter Property="Padding" Value="8,6"/>
  <Setter Property="Width" Value="140"/>
</Style>

<!-- 节点状态 -->
<Style Selector="Border.taskgraph-live-node.running">
  <Setter Property="BorderBrush" Value="#FF6A00"/>
  <Setter Property="BoxShadow" Value="0 0 0 2 #FF6A0033"/>
</Style>
<Style Selector="Border.taskgraph-live-node.completed">
  <Setter Property="BorderBrush" Value="#18A558"/>
</Style>
<Style Selector="Border.taskgraph-live-node.failed">
  <Setter Property="BorderBrush" Value="#E5484D"/>
</Style>

<!-- 策略徽章 -->
<Style Selector="Border.strategy-badge">
  <Setter Property="CornerRadius" Value="3"/>
  <Setter Property="Padding" Value="4,2"/>
  <Setter Property="FontSize" Value="10"/>
</Style>
<Style Selector="Border.strategy-badge.child-session">
  <Setter Property="Background" Value="#E6F0FF"/>
  <Setter Property="Foreground" Value="#2459B8"/>
</Style>
<Style Selector="Border.strategy-badge.in-session">
  <Setter Property="Background" Value="#FFF5ED"/>
  <Setter Property="Foreground" Value="#FF6A00"/>
</Style>
<Style Selector="Border.strategy-badge.inline">
  <Setter Property="Background" Value="#F0F0F3"/>
  <Setter Property="Foreground" Value="#40444B"/>
  <Setter Property="BorderBrush" Value="#C5C8CE"/>
  <Setter Property="BorderThickness" Value="1,0,0,0"/>
</Style>
```

### 5.7 动画

- 节点 Running 态:1s 循环的 border pulse(`Opacity 0.6 → 1.0`)
- 边:从源到目标的虚线流动(`StrokeDashOffset` 动画),5s 循环
- Panel 挂载:`TranslateTransform Y 100px → 0` + `Opacity 0 → 1`,200ms ease-out
- Self-Mutation 应用:变更节点 600ms `Background` highlight 闪烁

---

## 6. 架构与数据流

### 6.1 扩展后的层次

```text
┌────────────────────────── UI ──────────────────────────┐
│ ChatWorkspaceControl (扩展)                              │
│  ├─ Header                                                 │
│  ├─ TaskGraphLivePanelControl (新增)                       │
│  └─ MessageList (现有)                                     │
│ TaskGraphWorkspaceControl (沿用 v1/v2,独立工作区)           │
│  └─ 离线规划场景,不再强制 run,可选"切换到对话中"按钮         │
└─────────────────────────── ↓ ───────────────────────────┘
┌────────────────────── ViewModel ───────────────────────┐
│ ChatWorkspaceViewModel (扩展)                             │
│  ├─ ActiveGraph: TaskGraph? (新增)                       │
│  ├─ LivePanel: TaskGraphLivePanelViewModel? (新增)        │
│  ├─ TriggerAutoTaskGraphAsync(text) (新增)                │
│  ├─ TriggerTemplateTaskGraphAsync(kind, input) (新增)     │
│  └─ 订阅 TaskGraphRuntimeHub 全部事件 (新增)              │
│ TaskGraphWorkspaceViewModel (沿用)                        │
│ TaskGraphLivePanelViewModel (新增)                       │
│  ├─ 订阅 TaskGraphRuntimeHub                                │
│  ├─ 维护 LiveNodes / LiveEdges                             │
│  └─ NodeClick → 请求弹窗                                   │
└─────────────────────────── ↓ ───────────────────────────┘
┌────────────────────── Services ────────────────────────┐
│ TaskGraphPlanner (扩展:写入 DelegationStrategy + Origin) │
│ TaskGraphExecutor (扩展:按 strategy 选路径)              │
│ TaskGraphRuntimeHub (扩展:加 SelfMutated / StrategyResolved 事件) │
│ ITaskGraphMutationApplier (新增)                          │
│ IAgentGateway (扩展:加 CreateChildSessionAsync / DelegateAsSubagentAsync) │
│ OpenCodeAgentGateway (实现新接口,使用 ParentID)         │
└─────────────────────────── ↓ ───────────────────────────┘
┌────────────────────── Cross-cutting ───────────────────┐
│ Models.TaskGraph.TaskNode (扩展:DelegationStrategy + ParentSessionId) │
│ Models.TaskGraph.TaskGraph (扩展:OriginHint)             │
│ OpenCode.Client (已支持 ParentID,无需改)                  │
└──────────────────────────────────────────────────────────┘
```

### 6.2 端到端数据流(自动模式)

```text
User: 在 chat 输入 /plan 帮我做一个 todo app
  ↓
ChatWorkspaceVM.TriggerAutoTaskGraphAsync(text)
  ├─ IsStreaming = true
  ├─ var graph = await TaskGraphPlanner.CreateFromIntentAsync(text, ...)
  │    ├─ LlmPlanner.PromptAsync(...)
  │    ├─ JsonParsingService.ParseWithRetry(...)
  │    ├─ graph.OriginHint = TaskGraphOriginHint.ChatAuto
  │    └─ 每个节点 ApplySuggestStrategy(节点, graph)  // 写入 DelegationStrategy
  ├─ graph.Persist(...)
  ├─ LivePanel = new TaskGraphLivePanelViewModel(graph, ...)
  ├─ LivePanel.AttachToChat()
  └─ TaskGraphExecutor.ExecuteAsync(graph, request, OnStrategyChanged, OnSelfMutated)
       │
       ├─ 对每个 node:
       │    ├─ PublishStrategyResolved(graph.Id, node.Id, strategy)  // LivePanel 更新徽章
       │    ├─ switch (node.DelegationStrategy) {
       │    │     case NewSession:
       │    │       sessionId = await agent.CreateSessionAsync(...)
       │    │       break
       │    │     case ChildSession:
       │    │       parentId = graph.RootSessionId ?? await PrepareParentSession(...)
       │    │       sessionId = await agent.CreateChildSessionAsync(parentId, ...)
       │    │       break
       │    │     case InSessionSubagent:
       │    │       sessionId = graph.ConversationSessionId  // 复用 chat session
       │    │       break
       │    │     case Inline:
       │    │       sessionId = graph.ConversationSessionId  // 复用 chat session
       │    │       break
       │    │  }
       │    ├─ node.AgentSessionId = sessionId
       │    ├─ await foreach (var chunk in agent.SendMessageAsync(sessionId, ...))
       │    │     runtimeHub.PublishChunk(...)
       │    └─ after completion:
       │          ├─ result.Messages = ...
       │          ├─ if (node.Kind == Plan) await mutationApplier.TryApplyAsync(...)
       │          └─ runtimeHub.PublishNodeChanged(...)
       └─ 完成,LivePanel 标记 Completed
```

### 6.3 模型扩展

```csharp
// Models/TaskGraph/TaskNode.cs 扩展
[ObservableProperty]
private TaskNodeDelegationStrategy _delegationStrategy = TaskNodeDelegationStrategy.NewSession;

[ObservableProperty]
private string? _parentSessionId;  // ChildSession 时存父 session id;null = 顶层

[ObservableProperty]
private bool _mutationEnabled = false;  // 显式启用自我调节
```

```csharp
// Models/TaskGraph/TaskGraph.cs 扩展
[ObservableProperty]
private TaskGraphOriginHint _originHint = TaskGraphOriginHint.WorkspaceDirect;

[ObservableProperty]
private string? _conversationSessionId;  // 自动模式时存 chat session id (供 InSessionSubagent/Inline 复用)
```

### 6.4 IAgentGateway 扩展

```csharp
// Services/Agent/IAgentGateway.cs 扩展
public interface IAgentGateway
{
    // 现有方法保持
    Task<string> CreateSessionAsync(SessionCreateRequest request, CancellationToken ct = default);
    // ...

    // 新增
    /// <summary>
    /// 创建子 session(共享父项目,独立上下文)。
    /// 实现:在 OpenCodeAgentGateway 内部,使用 OpenCode.Client 的 SessionCreateRequest.ParentID
    /// </summary>
    Task<string> CreateChildSessionAsync(
        string parentSessionId,
        SessionCreateRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// 列出父 session 的子 session。
    /// 实现:OpenCodeClient 已暴露 GET /session/{id}/children,直接调。
    /// </summary>
    Task<IReadOnlyList<RemoteSessionInfo>> ListChildSessionsAsync(
        string parentSessionId,
        CancellationToken ct = default);
}
```

### 6.5 TaskGraphRuntimeHub 扩展

```csharp
public interface ITaskGraphRuntimeHub
{
    // 现有
    event EventHandler<TaskGraphChunkEventArgs>? ChunkReceived;
    event EventHandler<TaskGraphNodeEventArgs>? NodeChanged;

    // 新增
    event EventHandler<TaskGraphStrategyEventArgs>? StrategyResolved;
    event EventHandler<TaskGraphMutationEventArgs>? SelfMutated;
    event EventHandler<TaskGraphExecutionEventArgs>? ExecutionStateChanged;

    void PublishStrategy(string graphId, string nodeId, TaskNodeDelegationStrategy strategy);
    void PublishMutation(string graphId, MutationApplyResult result);
    void PublishExecutionState(string graphId, TaskGraphExecutionState state);
}
```

---

## 7. 决策记录(锁定清单)

| 决策点 | 选择 | 备注 |
|---|---|---|
| 委托策略枚举 | 4 个:NewSession / ChildSession / InSessionSubagent / Inline | 见 §3.1 |
| 默认策略 | NewSession(向后兼容) | 见 §3.2 |
| 策略选择 | LLM 默认 + UI 可改 | 见 §3.3 |
| 自动模式入口 | chat 工具栏按钮 + /plan 命令 | 见 §4.2.1 |
| 自我调节触发 | Plan 节点完成 + mutation 标记 | 见 §4.2.3 |
| 自我调节次数 | 每节点最多 1 次 | 避免递归 |
| 自我调节确认 | 默认 auto-apply + 5s 撤销 | 见 §4.2.4 |
| 模板模式入口 | chat 工具栏下拉 | 见 §4.3.1 |
| Bug 列表子策略 | ChildSession(共享父背景) | 节省 token |
| 实时 UI 位置 | chat 头部下方内嵌 panel | 见 §4.4 |
| 实时 UI 高度 | 自适应 60-360px | 避免抢屏 |
| 节点策略徽章 | 角落小徽章,点击切换 | 见 §4.4.4 |
| 节点详情 | hover popover + click 弹窗 | 见 §4.4.3 |
| 子会话卡片 | 复用现有 SubagentActivityCardControl | 见 §5.3 |
| 独立 TaskGraph 工作区 | **保留**(可选"切换到对话中"按钮) | 向后兼容 |
| OpenCode SDK 扩展 | 使用现有 ParentID 字段(SDK 已支持) | 见 §2.1 |
| 命名 | 沿用 v1/v2 命名 | TaskGraphDelegationStrategy / TaskGraphOriginHint |
| v1/v2 锁定 | 不修改 | 仅扩展 |
| 数据持久化 | 沿用 JsonTaskGraphStore,schema 加 3 字段 | 向前兼容 |

---

## 8. 风险与缓解

| 风险 | 缓解 |
|---|---|
| ChildSession 误用导致子 agent 仍重读项目 | Bug 列表模板固定 n0 节点为"准备背景",后续节点复用 |
| InSessionSubagent 与父 chat 流竞争 | 节点执行期间 chat composer 禁用(见 §5.4) |
| 自我调节产生死循环 | 每节点 max 1 次 + 全图 max 5 次 mutation |
| Chat 流被 TaskGraph 节点输出淹没 | 节点运行中输出只走 panel,完成后才汇总到 chat |
| 多 panel 同时挂载(用户连点按钮) | 单例:同时仅一个 ActiveGraph,新触发替换旧的并 cancel 旧 executor |
| 节点策略运行时切换导致数据不一致 | UI 灰显运行中节点的策略菜单(见 §4.4.4) |
| ChildSession 在 OpenCode 后端的权限继承行为不明 | 实施前压测,确认权限/工具集是否继承;若否,需在 v3.1 调整 |
| LLM 在自我调节时改图破坏 DAG 结构 | MutationApplier 内置 cycle 检测 + 拓扑校验,违规则拒绝应用 |
| 大图(>50 节点)实时 panel 性能 | 复用 v1 已有 bulk-replace 优化 + 节点 canvas 化简版(无 ports 无内部 hover 区) |

---

## 9. 技术债清单(后续阶段处理)

| 债项 | 原因 | 后续 |
|---|---|---|
| 节点级策略 UI 提示气泡 | v3 默认 AI 选,UI 不暴露解释 | v3.1 |
| 自我调节可视化编辑器 | v3 只允许 AI 改 | v3.2 |
| 多级嵌套 subagent (>2 层) | v3 仅 2 层(parent + child) | v4 |
| TaskGraph "暂停-迁移-恢复" 跨机器 | v3 单机 | v4 |
| OpenCode Plugin 机制集成 | v3 不做 | v4 |
| Chat 历史自动归档(防止子 agent 输出污染主对话) | v3 用 panel 隔离 | v3.1 |
| 策略学习(从历史回放推断最佳策略) | v3 规则 | v4 |

---

## 10. 兼容性说明

### 10.1 与 v1 的兼容
- 模型 schema 加 3 字段,JsonTaskGraphStore 反序列化时用默认值
- v1 老图加载后,所有节点 `DelegationStrategy = NewSession`(默认值),行为完全不变

### 10.2 与 v2 的兼容
- TaskGraphExecutor 4 个现有方法(`ExecuteAsync` / `RetryFailedAsync` / `ContinueAsync` / `ReconcileAsync`)签名不变
- 内部按 strategy 选路径,NewSession 路径与 v2 完全一致
- 4 个输入模式 API 不变,只是 `CreateFromIntentAsync` / `CreateFromDocumentAsync` / `CreateTemplateGraphAsync` 内部多一步"写入 strategy"

### 10.3 与 Chat 的兼容
- ChatWorkspaceViewModel 新增属性 `ActiveGraph` / `LivePanel`,不影响现有 chat 流
- Composer 禁用逻辑只针对"图执行中",正常对话不受影响
- SubagentActivityCardControl 完全复用,不修改

### 10.4 与 OpenCode 的兼容
- 不修改 OpenCode SDK,只使用已暴露的 ParentID / children 字段
- 不引入 OpenCode plugin 机制
- 不修改 OpenCode 后端协议

---

## 11. 文档交付

| 时机 | 文档 | 路径 |
|---|---|---|
| 已完成 | v1 方案 | `Docs/working/TaskGraph-方案.md` |
| 已完成 | v1 改造计划 | `Docs/working/TaskGraph-改造计划.md` |
| 已完成 | v2 编排方案 | `Docs/working/TaskGraph-编排方案.md` |
| **本阶段(已完成)** | **v3 委托与对话内编排方案** | **`Docs/working/TaskGraph-委托与对话内编排-方案.md`(本文件)** |
| 本阶段(已完成) | v3 改造计划 | `Docs/working/TaskGraph-委托与对话内编排-改造计划.md` |
| v3 完成后 | AGENTS.md 更新 | 增加 "v3 委托策略" + "对话内 TaskGraph" 章节 |
| v3 完成后 | `Docs/developer/architecture.md` 更新 | 补充 v3 相关架构 |

---

## 12. 变更日志

| 日期 | 版本 | 变更 |
|---|---|---|
| 2026-06-16 | v1.0 | 画图工具 + 节点编辑 + 持久化 |
| 2026-06-19 | v2.0 | 三种输入模式 + 节点执行 + 弹窗 |
| 2026-06-21 | v3.0 | 节点级委托策略 + 对话内 TaskGraph + 实时 UI |
