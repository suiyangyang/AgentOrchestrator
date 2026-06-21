# TaskGraph 委托策略与对话内编排 改造计划 (v3)

> 状态: 方案已锁定,待执行
> 决策日期: 2026-06-21
> 范围: 实施 [`TaskGraph-委托与对话内编排-方案.md`](./TaskGraph-委托与对话内编排-方案.md) 的阶段任务、文件清单、子任务、验证步骤、技术债
> 配套文档:
> - [`TaskGraph-委托与对话内编排-方案.md`](./TaskGraph-委托与对话内编排-方案.md) (v3, 已锁定):架构/模型/UI/数据流
> - [`TaskGraph-方案.md`](./TaskGraph-方案.md) (v1, 已锁定)
> - [`TaskGraph-改造计划.md`](./TaskGraph-改造计划.md) (v1, 已锁定)
> - [`TaskGraph-编排方案.md`](./TaskGraph-编排方案.md) (v2, 已锁定)
> - **本文 (v3)**:在 v1/v2 之上叠加委托策略 + 对话内编排的实施计划

---

## 0. 阅读指引

| 想了解 | 读哪份 |
|---|---|
| 为什么这样做 / 架构 | `TaskGraph-委托与对话内编排-方案.md` |
| **怎么落地** | **本文** |
| v1 画图 / v2 编排基础 | 对应方案的 v1/v2 |

---

## 1. 项目基线现状

| 项 | 当前状态 |
|---|---|
| `TaskGraphExecutor` | 402 行,每节点 `CreateSessionAsync`,固定 NewSession |
| `TaskGraphRuntimeHub` | 16 行,2 个事件(ChunkReceived, NodeChanged) |
| `IAgentGateway` | 112 行,无 ChildSession / Subagent 接口 |
| `OpenCodeAgentGateway` | 已用 `GetSubagentActivitiesAsync` 拉 children,但 `CreateSessionAsync` 未传 ParentID |
| `TaskNode` | 171 行,无 DelegationStrategy / ParentSessionId |
| `TaskGraph` | 已有 Mode / ExecutionState,无 OriginHint / ConversationSessionId |
| `ChatWorkspaceViewModel` | 1530 行,有 `SubagentActivities` 集合,无 ActiveGraph |
| `ChatWorkspaceControl` | 有 composer 工具栏,无 [智能编排]/[模板编排] 按钮 |
| `SubagentActivityCardControl` | 已存在,显示右侧 subagent 卡片 |
| `TaskGraphWorkspaceControl` | 独立工作区已完整,可继续运行 |
| `TaskGraphBugStructuredParser` | v2 已实现,结构化 bug 报告 |
| OpenCode SDK | `Session.ParentID` + `SessionCreateRequest.ParentID` + `GET /session/{id}/children` 已暴露 |
| 模板 | 3 个(task-list / feature-dev / bug-list) |
| 项目成熟度 | **Disciplined** — 严格遵循 MVVM + CompiledBindings + 类选择器 Style |

---

## 2. 改动清单(完整文件级)

### 2.1 新增枚举 / 模型

| 路径 | 内容 |
|---|---|
| `Models/TaskGraph/TaskNodeDelegationStrategy.cs` | `enum { NewSession, ChildSession, InSessionSubagent, Inline }` |
| `Models/TaskGraph/TaskGraphOriginHint.cs` | `enum { WorkspaceDirect, ChatAuto, ChatTemplate, ChatMention }` |
| `Models/TaskGraph/TaskGraphMutation.cs` | `record MutationApplyResult(Applied, NodesAdded, NodesRemoved, EdgesAdded, Reason)` |

### 2.2 修改文件(模型扩展)

| 路径 | 改动 |
|---|---|
| `Models/TaskGraph/TaskNode.cs` | 加 `DelegationStrategy` / `ParentSessionId` / `MutationEnabled` 字段 |
| `Models/TaskGraph/TaskGraph.cs` | 加 `OriginHint` / `ConversationSessionId` 字段 |

### 2.3 修改文件(Services)

| 路径 | 改动 |
|---|---|
| `Services/Agent/IAgentGateway.cs` | 加 `CreateChildSessionAsync` / `ListChildSessionsAsync` 接口 |
| `Services/Agent/OpenCodeAgentGateway.cs` | 实现 `CreateChildSessionAsync`(用 `SessionCreateRequest.ParentID`) |
| `Services/Agent/NotImplementedAgentGateway.cs` | 实现桩(throw `NotImplementedException`) |
| `Services/TaskGraph/ITaskGraphRuntimeHub.cs` | 加 `StrategyResolved` / `SelfMutated` / `ExecutionStateChanged` 事件 |
| `Services/TaskGraph/TaskGraphRuntimeHub.cs` | 实现新事件 + Publish* 方法 |
| `Services/TaskGraph/ITaskGraphExecutor.cs` | 加 `OnStrategyChanged` / `OnSelfMutated` 回调参数(可选) |
| `Services/TaskGraph/TaskGraphExecutor.cs` | 重构 `ExecuteNodeAsync` 按 strategy 选路径 |
| `Services/TaskGraph/TaskGraphStrategyResolver.cs` | **新增**:实现 `SuggestStrategy` 决策函数 |
| `Services/TaskGraph/ITaskGraphMutationApplier.cs` | **新增**:mutation 应用接口 |
| `Services/TaskGraph/TaskGraphMutationApplier.cs` | **新增**:默认实现,带 DAG 校验 |
| `Services/TaskGraph/ITaskGraphPlanner.cs` | 加新方法 `CreateFromChatAutoAsync(text, conversationSessionId, ...)` |
| `Services/TaskGraph/DefaultTaskGraphPlanner.cs` | 扩展现有 3 个方法,内部调用 `TaskGraphStrategyResolver` |

### 2.4 修改文件(ViewModels)

| 路径 | 改动 |
|---|---|
| `ViewModels/ChatWorkspaceViewModel.cs` | 加 `ActiveGraph` / `LivePanel` 属性 + `TriggerAutoTaskGraphAsync` / `TriggerTemplateTaskGraphAsync` 方法 + 订阅 `TaskGraphRuntimeHub` |
| `ViewModels/TaskGraphWorkspaceViewModel.cs` | 加"切换到对话中"命令 + `OpenGraphInChatAsync` 方法 |
| `ViewModels/TaskGraphLivePanelViewModel.cs` | **新增**:实时面板 VM,订阅 `TaskGraphRuntimeHub`,维护 LiveNodes / LiveEdges |
| `ViewModels/TaskGraphLiveNodeViewModel.cs` | **新增**:live 面板内的单节点 VM |
| `ViewModels/TaskGraphLiveEdgeViewModel.cs` | **新增**:live 面板内的边 VM |
| `ViewModels/StrategyBadgeViewModel.cs` | **新增**:策略徽章 VM |
| `ViewModels/BugListInputViewModel.cs` | **新增**:Bug 列表输入弹层 VM |
| `ViewModels/GraphMutationConfirmViewModel.cs` | **新增**:mutation 确认弹层 VM |

### 2.5 新增文件(Controls)

| 路径 | 内容 |
|---|---|
| `Controls/TaskGraphLivePanelControl.axaml(.cs)` | 实时面板主控件 |
| `Controls/TaskGraphLiveNodeControl.axaml(.cs)` | live 节点卡片(轻量版,无 ports) |
| `Controls/TaskGraphLiveEdgeControl.axaml(.cs)` | live 边(自绘,带虚线流动) |
| `Controls/StrategyBadgeControl.axaml(.cs)` | 策略徽章 |
| `Controls/NodeDetailPopoverControl.axaml(.cs)` | 节点详情轻量 popover |
| `Views/BugListInputWindow.axaml(.cs)` | Bug 列表输入窗口 |
| `Views/GraphMutationConfirmWindow.axaml(.cs)` | mutation 确认窗口 |

### 2.6 修改文件(Views / Controls)

| 路径 | 改动 |
|---|---|
| `Controls/ChatWorkspaceControl.axaml` | 工具栏扩 2 列: [智能编排] / [模板编排] |
| `Controls/ChatWorkspaceControl.axaml` | 头部下方加 `TaskGraphLivePanelControl` 挂载点 |
| `Controls/TaskGraphWorkspaceControl.axaml` | 顶部加 [在对话中打开] 按钮 |
| `App.axaml` | 追加 §5.6 的 Style |

### 2.7 修改文件(DI)

| 路径 | 改动 |
|---|---|
| `App.axaml.cs` | 注册新 VM / Service(`TaskGraphStrategyResolver` / `TaskGraphMutationApplier` / `TaskGraphLivePanelViewModel` 等) |
| `App.axaml.cs` | `OnFrameworkInitializationCompleted` 末尾:`ChatWorkspaceViewModel` 订阅 `TaskGraphRuntimeHub` 全部事件 |

### 2.8 新增事件参数(运行时事件)

| 路径 | 内容 |
|---|---|
| `Services/TaskGraph/TaskGraphStrategyEventArgs.cs` | `(GraphId, NodeId, Strategy)` |
| `Services/TaskGraph/TaskGraphMutationEventArgs.cs` | `(GraphId, MutationApplyResult)` |
| `Services/TaskGraph/TaskGraphExecutionEventArgs.cs` | `(GraphId, ExecutionState)` |

### 2.9 JSON 持久化扩展

| 路径 | 改动 |
|---|---|
| `Services/TaskGraph/JsonTaskGraphStore.cs` | schema 加 `delegationStrategy` / `parentSessionId` / `mutationEnabled` / `originHint` / `conversationSessionId`,反序列化时容错老 v1/v2 文件 |

---

## 3. DI 接线(代码片段)

`App.axaml.cs : ConfigureServices` 增量:

```csharp
// ── 已有,保留 ──
services.AddSingleton<ITaskGraphRuntimeHub, TaskGraphRuntimeHub>();
services.AddSingleton<ITaskGraphExecutor, TaskGraphExecutor>();
services.AddSingleton<ITaskGraphPlanner, DefaultTaskGraphPlanner>();

// ── v3 新增 ──
services.AddSingleton<TaskGraphStrategyResolver>();
services.AddSingleton<ITaskGraphMutationApplier, TaskGraphMutationApplier>();

// VM(已有 ChatWorkspaceViewModel 改,扩展属性即可,无需新注册)
services.AddSingleton<ChatWorkspaceViewModel>();
```

`OnFrameworkInitializationCompleted` 末尾增量:

```csharp
var chat = provider.GetRequiredService<ChatWorkspaceViewModel>();
var graph = provider.GetRequiredService<TaskGraphWorkspaceViewModel>();
var runtimeHub = provider.GetRequiredService<ITaskGraphRuntimeHub>();
var livePanel = provider.GetRequiredService<TaskGraphLivePanelViewModel>();

// v1 已有
chat.RequestExtractToGraph += graph.HandleChatExtractRequest;

// v3 新增:chat 订阅 runtime hub 全部事件
runtimeHub.ChunkReceived += livePanel.OnChunk;
runtimeHub.NodeChanged += livePanel.OnNodeChanged;
runtimeHub.StrategyResolved += livePanel.OnStrategyResolved;
runtimeHub.SelfMutated += livePanel.OnSelfMutated;
runtimeHub.ExecutionStateChanged += livePanel.OnExecutionStateChanged;
```

---

## 4. 实施阶段(Phase A–G)

| Phase | 范围 | 验收 | 估时 |
|---|---|---|---|
| **A** | 模型 + 枚举 + IAgentGateway 扩展 | build 干净,DelegationStrategy / ChildSession API 可调 | 0.5d |
| **B** | Strategy Resolver + Mutation Applier | 单元测试 8+ 用例通过 | 0.5d |
| **C** | Runtime Hub 扩展 + Executor 重构 | 4 种 strategy 都能跑通示例图 | 1.0d |
| **D** | Live Panel VM + 控件 + App.axaml 样式 | panel 可订阅事件并刷新,5s 内节点状态可见 | 1.5d |
| **E** | Chat 工具栏 + BugListInput + GraphMutationConfirm 弹层 | chat 触发 [智能编排] 可见 panel;bug list 输入可生成图 | 1.0d |
| **F** | 自我调节(mutation)端到端 | Plan 节点完成触发 mutation 弹窗,应用/拒绝正确 | 0.5d |
| **G** | 持久化扩展 + 验证 + 文档 | 5 条主路径全过;AGENTS.md / architecture.md 更新 | 0.5d |

> **总计约 5.5 工作日**(单人串行)。如并行派 subagent 可压缩到 2-3 天。

### 4.1 Phase A: 模型 + 枚举 + IAgentGateway 扩展(0.5d)

**子任务**:

| # | 任务 | 估时 |
|---|---|---|
| A1 | 新增 `TaskNodeDelegationStrategy` 枚举 | 0.5h |
| A2 | 新增 `TaskGraphOriginHint` 枚举 | 0.25h |
| A3 | 新增 `TaskGraphMutation` record | 0.25h |
| A4 | `TaskNode` 加 3 字段(`DelegationStrategy` / `ParentSessionId` / `MutationEnabled`) | 0.5h |
| A5 | `TaskGraph` 加 2 字段(`OriginHint` / `ConversationSessionId`) | 0.5h |
| A6 | `IAgentGateway` 加 2 方法(`CreateChildSessionAsync` / `ListChildSessionsAsync`) | 0.5h |
| A7 | `OpenCodeAgentGateway` 实现 2 方法(用 `SessionCreateRequest.ParentID` + `GET /session/{id}/children`) | 1.0h |
| A8 | `NotImplementedAgentGateway` 加桩(throw) | 0.25h |

**验证**:
- `dotnet build` 干净,0 warning
- 单元测试:模拟 OpenCode 后端响应,验证 `CreateChildSessionAsync(parentId, request)` 真的传了 `ParentID` 字段

**关键文件**:
- 新增:`Models/TaskGraph/TaskNodeDelegationStrategy.cs` / `TaskGraphOriginHint.cs` / `TaskGraphMutation.cs`
- 改:`Models/TaskGraph/TaskNode.cs` / `TaskGraph.cs` / `Services/Agent/IAgentGateway.cs` / `Services/Agent/OpenCodeAgentGateway.cs` / `Services/Agent/NotImplementedAgentGateway.cs`

**派发建议**:
- 1 个 `unspecified-high` agent(纯数据/服务层),串行做 A1-A8

### 4.2 Phase B: Strategy Resolver + Mutation Applier(0.5d)

**子任务**:

| # | 任务 | 估时 |
|---|---|---|
| B1 | 新增 `TaskGraphStrategyResolver`(纯静态类,无副作用) | 1.0h |
| B2 | 决策函数单元测试:8+ 用例覆盖每个 rule | 1.0h |
| B3 | 新增 `ITaskGraphMutationApplier` 接口 | 0.25h |
| B4 | 新增 `DefaultTaskGraphMutationApplier`,内含:mutation JSON 解析 / DAG cycle 校验 / 拓扑校验 | 1.5h |
| B5 | Mutation 应用单元测试:6+ 用例(含 cycle 拒绝、孤立节点拒绝) | 1.0h |

**关键文件**:
- 新增:`Services/TaskGraph/TaskGraphStrategyResolver.cs` / `ITaskGraphMutationApplier.cs` / `DefaultTaskGraphMutationApplier.cs`
- 测试:`Services/TaskGraph/TaskGraphStrategyResolverTests.cs` / `TaskGraphMutationApplierTests.cs`

**派发建议**:
- 1 个 `ultrabrain` agent(算法 + 测试)

### 4.3 Phase C: Runtime Hub 扩展 + Executor 重构(1.0d)

**子任务**:

| # | 任务 | 估时 |
|---|---|---|
| C1 | `ITaskGraphRuntimeHub` 加 3 事件(`StrategyResolved` / `SelfMutated` / `ExecutionStateChanged`) | 0.5h |
| C2 | `TaskGraphRuntimeHub` 实现新事件 + Publish* 方法 | 0.5h |
| C3 | 新增 3 个 EventArgs 类 | 0.5h |
| C4 | `TaskGraphExecutor.ExecuteNodeAsync` 重构为按 `DelegationStrategy` 4 路分支 | 3.0h |
| C5 | 在策略选择前调 `TaskGraphStrategyResolver.SuggestStrategy`(若 `node.DelegationStrategy == NewSession` 且 `graph.OriginHint != WorkspaceDirect`) | 0.5h |
| C6 | 在 Plan 节点完成后调 `MutationApplier.TryApplyAsync`,若 `Applied` 则 publish `SelfMutated` 事件 | 1.0h |
| C7 | ChildSession 路径:实现 `PrepareParentSession` helper(为 `n0` 类节点准备共享背景) | 1.0h |
| C8 | InSessionSubagent / Inline 路径:复用 `graph.ConversationSessionId` | 0.5h |

**关键文件**:
- 改:`Services/TaskGraph/ITaskGraphRuntimeHub.cs` / `TaskGraphRuntimeHub.cs` / `ITaskGraphExecutor.cs` / `TaskGraphExecutor.cs`
- 新增:`Services/TaskGraph/TaskGraphStrategyEventArgs.cs` / `TaskGraphMutationEventArgs.cs` / `TaskGraphExecutionEventArgs.cs`

**派发建议**:
- 1 个 `deep` agent(改动大,需理解现有 executor)
- 或拆为 2 个并行:`unspecified-high`(C1-C3 事件)+ `deep`(C4-C8 executor 重构)

### 4.4 Phase D: Live Panel VM + 控件 + App.axaml 样式(1.5d)

**子任务**:

| # | 任务 | 估时 |
|---|---|---|
| D1 | `TaskGraphLivePanelViewModel`:订阅 5 个 runtime hub 事件,维护 `LiveNodes` / `LiveEdges` 集合 | 2.0h |
| D2 | `TaskGraphLiveNodeViewModel`:暴露 `Status` / `Title` / `Progress` / `Strategy` / `LastLogLine` / `TokenCount` | 1.0h |
| D3 | `TaskGraphLiveEdgeViewModel`:暴露 `FromId` / `ToId` / `State` (Ready/Animating/Expired) | 0.5h |
| D4 | `StrategyBadgeViewModel`:暴露 `Strategy` / `Color` / `Icon` / `Tooltip` | 0.5h |
| D5 | `TaskGraphLivePanelControl.axaml`:布局(头部状态条 + 简化画布 + 节点 + 边) | 2.0h |
| D6 | `TaskGraphLiveNodeControl.axaml`:轻量节点卡片(无 ports,140x60px) | 1.5h |
| D7 | `TaskGraphLiveEdgeControl.axaml`:自绘边,虚线流动动画 | 1.5h |
| D8 | `StrategyBadgeControl.axaml`:4 种策略徽章样式 | 0.5h |
| D9 | `NodeDetailPopoverControl.axaml`:hover 节点弹出,含 status/progress/log 摘要 + 完整详情按钮 | 1.0h |
| D10 | `App.axaml` 加 §5.6 样式 | 0.5h |

**关键文件**:
- 新增:`ViewModels/TaskGraphLivePanelViewModel.cs` / `TaskGraphLiveNodeViewModel.cs` / `TaskGraphLiveEdgeViewModel.cs` / `StrategyBadgeViewModel.cs`
- 新增:`Controls/TaskGraphLivePanelControl.axaml(.cs)` / `TaskGraphLiveNodeControl.axaml(.cs)` / `TaskGraphLiveEdgeControl.axaml(.cs)` / `StrategyBadgeControl.axaml(.cs)` / `NodeDetailPopoverControl.axaml(.cs)`
- 改:`App.axaml`

**派发建议**:
- 1 个 `visual-engineering` agent(主攻 D5-D9 UI 控件 + D10 样式)
- 1 个 `unspecified-high` agent(主攻 D1-D4 VM)
- **并行**派发,完成后 D1-D4 给 visual-engineering 用

### 4.5 Phase E: Chat 工具栏 + BugListInput + GraphMutationConfirm 弹层(1.0d)

**子任务**:

| # | 任务 | 估时 |
|---|---|---|
| E1 | `ChatWorkspaceViewModel` 加 `ActiveGraph` / `LivePanel` 属性 | 0.5h |
| E2 | `ChatWorkspaceViewModel.TriggerAutoTaskGraphAsync(text)`:挂载 panel + 启动 executor | 2.0h |
| E3 | `ChatWorkspaceViewModel.TriggerTemplateTaskGraphAsync(kind, input)`:模板分支 | 1.0h |
| E4 | `ChatWorkspaceViewModel` 订阅 `TaskGraphRuntimeHub` 全部事件,转发到 `LivePanel` | 1.0h |
| E5 | `ChatWorkspaceControl.axaml` 工具栏扩 2 列([智能编排] / [模板编排]) | 1.0h |
| E6 | `ChatWorkspaceControl.axaml` 头部下方加 `TaskGraphLivePanelControl` 挂载点(可见性绑定 `LivePanel != null`) | 0.5h |
| E7 | `BugListInputViewModel`:输入 + 解析预览(智能解析 mode 用 LLM 调一次,手动校对 mode 用规则) | 1.0h |
| E8 | `BugListInputWindow.axaml`:弹层 UI | 0.5h |
| E9 | `GraphMutationConfirmViewModel`:展示 mutation 详情(增/删/改 + reason)+ [应用]/[拒绝] 按钮 | 0.5h |
| E10 | `GraphMutationConfirmWindow.axaml`:弹层 UI | 0.5h |
| E11 | `TaskGraphWorkspaceViewModel` 加 `OpenGraphInChatAsync` 命令 | 0.5h |
| E12 | `TaskGraphWorkspaceControl.axaml` 加 [在对话中打开] 按钮 | 0.25h |
| E13 | `App.axaml.cs` 注册新 VM + DI 接线 | 0.5h |

**关键文件**:
- 改:`ViewModels/ChatWorkspaceViewModel.cs` / `TaskGraphWorkspaceViewModel.cs` / `Controls/ChatWorkspaceControl.axaml` / `Controls/TaskGraphWorkspaceControl.axaml` / `App.axaml.cs`
- 新增:`ViewModels/BugListInputViewModel.cs` / `GraphMutationConfirmViewModel.cs` / `Views/BugListInputWindow.axaml(.cs)` / `Views/GraphMutationConfirmWindow.axaml(.cs)`

**派发建议**:
- 1 个 `deep` agent(E1-E4 + E11-E13 业务逻辑)
- 1 个 `visual-engineering` agent(E5-E10 + E12 UI)
- **并行**

### 4.6 Phase F: 自我调节端到端(0.5d)

**子任务**:

| # | 任务 | 估时 |
|---|---|---|
| F1 | Plan 节点 prompt 增加模板:"If you discover the plan needs adjustment, output JSON: {\"taskgraph_mutation\": {...}}" | 0.5h |
| F2 | MutationApplier 集成测试:构造一个 Plan 节点 + 模拟返回 mutation JSON,验证 executor 应用并派发事件 | 1.5h |
| F3 | UI 集成:GraphMutationConfirm 弹窗从事件触发,确认/拒绝回写 graph | 1.0h |
| F4 | 自我调节计数器(每节点 max 1 次,全图 max 5 次) | 0.5h |
| F5 | Auto-apply 默认 5s + 可关闭(在 `ChatSettings` 加) | 0.5h |

**关键文件**:
- 改:`Services/TaskGraph/TaskGraphExecutor.cs` / `Services/TaskGraph/DefaultTaskGraphMutationApplier.cs`
- 新增:`Services/TaskGraph/PlanNodePromptTemplate.cs`(常量字符串)
- 改:`ViewModels/GraphMutationConfirmViewModel.cs`

**派发建议**:
- 1 个 `deep` agent(算法 + 集成)

### 4.7 Phase G: 持久化扩展 + 验证 + 文档(0.5d)

**子任务**:

| # | 任务 | 估时 |
|---|---|---|
| G1 | `JsonTaskGraphStore` schema 扩展:加 5 字段(`delegationStrategy` / `parentSessionId` / `mutationEnabled` / `originHint` / `conversationSessionId`) | 1.0h |
| G2 | 反序列化容错:缺失字段用默认值 | 0.5h |
| G3 | 单元测试:加载 v1 老图 + 加载 v2 老图,验证默认值正确 | 1.0h |
| G4 | 跑通 5 条主路径验证矩阵(见 §6) | 1.0h |
| G5 | 更新 `AGENTS.md` 增加 "v3 委托策略" + "对话内 TaskGraph" 章节 | 0.5h |
| G6 | 更新 `Docs/developer/architecture.md` §UI + §Layered Services 补充 v3 | 0.5h |
| G7 | `dotnet build -c Debug` 干净,0 warning | 0.25h |

**关键文件**:
- 改:`Services/TaskGraph/JsonTaskGraphStore.cs` / `AGENTS.md` / `Docs/developer/architecture.md`

**派发建议**:
- 主 orchestrator 自己跑 G4-G7(验证 + 文档)

---

## 5. Subagent 派发策略(总览)

| Phase | 派发方式 | 关键约束 |
|---|---|---|
| A | 1 个 `unspecified-high` 串行 | 不破坏 v2 模型,只扩展 |
| B | 1 个 `ultrabrain` | 必须含 8+ 单元测试,需提供测试运行命令 |
| C | 1 个 `deep` 或 2 个并行(事件 + executor) | executor 改动大,提供 v2 executor 行号引用 |
| D | 1 个 `unspecified-high` (VM) + 1 个 `visual-engineering` (UI) 并行 | visual-engineering 必须读 v1 TaskGraphWorkspaceControl.axaml 复用样式 |
| E | 1 个 `deep` (chat 业务) + 1 个 `visual-engineering` (UI) 并行 | 复用 §5.6 样式,新增时沿用项目 color tokens |
| F | 1 个 `deep` | 集成测试为主,提供 OpenCode mock |
| G | 主 orchestrator | — |

---

## 6. 验证矩阵(Phase G 必过)

### 6.1 编译验证

```bash
dotnet restore
dotnet build -c Debug
# 期望:0 error, 0 warning(允许的 warning 需在 AGENTS.md 注明)
```

### 6.2 主路径验证(v3 新增)

| 路径 | 操作 | 期望 |
|---|---|---|
| V3-P1 | chat 点 [智能编排],输入"帮我做一个 todo app" | panel 出现,显示 5+ 节点图,节点状态依次变化 |
| V3-P2 | chat 触发 [智能编排] 后,观察节点策略徽章 | 至少 1 个节点显示 ChildSession 徽章,1 个 Inline 徽章 |
| V3-P3 | chat 触发 [模板编排] → Bug 列表 → 粘贴 3 个 bug | 弹 BugListInputWindow,解析预览显示 3 个 bug,生成图含 n0 + 3×analyze + 3×fix + bug_report |
| V3-P4 | (模板生成后点执行)Bug 列表执行 | analyze 节点显示为 ChildSession(共享 n0 session),fix 节点也 ChildSession,bug_report 节点 Inline |
| V3-P5 | (运行中)chat 工具栏 [取消] | 当前节点自然完成,下游 Skipped,panel 标记 Cancelled |
| V3-P6 | (Plan 节点返回 taskgraph_mutation JSON) | 弹 GraphMutationConfirm 弹窗,5s 后 auto-apply,panel 高亮新节点 |
| V3-P7 | (节点 hover) | 节点微高亮 + 边强调 |
| V3-P8 | (节点 click) | 弹 NodeDetailPopover,显示 status + last 3 行日志 + [打开完整详情] |
| V3-P9 | (节点策略徽章 click) | 弹出策略切换菜单(运行中节点灰显) |
| V3-P10 | (独立工作区触发 [在对话中打开]) | 切到 chat tab,panel 出现并显示当前图,executor 不重启 |
| V3-P11 | 打开 v1 老图(无 DelegationStrategy 字段) | 所有节点 DelegationStrategy = NewSession(默认),行为不变 |
| V3-P12 | 打开 v2 老图(无 OriginHint) | OriginHint = WorkspaceDirect(默认),行为不变 |

### 6.3 v1/v2 主路径验证(回归)

| 路径 | 操作 | 期望 |
|---|---|---|
| P1 | 手动建图 + 持久化往返 | 同 v1 |
| P2 | 模式 ③ markdown 导入 | 同 v1/v2 |
| P3 | Chat 抽取 | 同 v1 |
| P4 | `/plan` 命令 | 同 v1 |
| P5 | Chat ↔ Graph 切换 | 同 v1 |
| P6 | 模式 ③ 导入 → 执行 → 节点状态实时更新 | 同 v2 |
| P7 | 点击节点 → 弹窗显示流式输出 | 同 v2 |
| P8 | 节点失败重试 3 次 + 下游 Skipped | 同 v2 |

### 6.4 边界情况

| 情况 | 期望 |
|---|---|
| ChildSession 父 session 不存在 | 节点 Failed,LastError = "父 session 不可用" |
| InSessionSubagent 节点但 graph.ConversationSessionId 为空 | 降级为 NewSession,Status 标 Warning |
| Mutation 引入 cycle | MutationApplier 拒绝,SelfMutated 事件不派发 |
| Mutation 引入孤立节点(无 depends_on 也不被依赖) | 允许(可能是用户后续手动连) |
| Chat 在 panel 挂载期间收到新用户消息 | composer 禁用,提示"编排进行中" |
| 用户连续点 [智能编排] | 单例:旧图 cancel,新图替换,旧 panel 折叠 |
| v1/v2 老 JSON 加载 | 缺失字段用默认值,行为完全向后兼容 |

---

## 7. 任务派发完整 Prompt 模板(给 subagent)

派发 Phase D visual-engineering 的 prompt 示例:

```text
TASK: 实现 TaskGraph 实时面板的 Avalonia UI 控件,5 个新控件。

EXPECTED OUTCOME:
- TaskGraphLivePanelControl.axaml(.cs) - 主面板,头部状态条 + 简化画布
- TaskGraphLiveNodeControl.axaml(.cs) - 轻量节点卡片 140x60px
- TaskGraphLiveEdgeControl.axaml(.cs) - 自绘边,带虚线流动动画
- StrategyBadgeControl.axaml(.cs) - 4 种策略徽章
- NodeDetailPopoverControl.axaml(.cs) - hover popover
- App.axaml 追加 §5.6 样式(完整 6 段)
- 每个控件的 .axaml.cs 必须有 partial class 继承自 UserControl
- 每个控件必须有 x:DataType 编译绑定
- 提供 dotnet build 验证截图

REQUIRED TOOLS: visual-engineering category,frontend-ui-ux skill, dotnet build, xaml inspector

MUST DO:
- 严格遵循 Avalonia 12 + CommunityToolkit.Mvvm 风格
- 复用 v1 TaskGraphWorkspaceControl.axaml 的色板和类选择器命名
- 每个 x:DataType 必须正确指向 VM
- Style 用类选择器(Selector="Border.taskgraph-live-node")
- 动画用 Avalonia 内建(Storyboard 或 KeyFrame),不引入第三方
- 节点的 IsSelected / IsRunning 状态用 DataTrigger 切类

MUST NOT DO:
- 不修改 v1/v2 现有 .axaml 文件
- 不引入新 NuGet 依赖
- 不改 OpenCode.Client
- 不在 code-behind 写业务逻辑(纯 UI plumbing)
- 不使用 Avalonia.WebView / WebView2

CONTEXT:
- 项目基线: E:\Work\Code\Tools\AgentOrchestrator
- v1 画布样式参考: src/AgentOrchestrator.App/Controls/TaskGraphWorkspaceControl.axaml
- v1 节点卡片参考: src/AgentOrchestrator.App/Controls/TaskGraphWorkspaceControl.axaml(找 .task-node 相关 Style)
- 方案文档: Docs/working/TaskGraph-委托与对话内编排-方案.md §5
- 待绑定的 VM 列表: TaskGraphLivePanelViewModel / TaskGraphLiveNodeViewModel / TaskGraphLiveEdgeViewModel / StrategyBadgeViewModel
- 颜色 tokens: #FF6A00 (running), #18A558 (completed), #E5484D (failed), #C5C8CE (pending), #9096A0 (skipped)
```

---

## 8. 风险与缓解

| 风险 | 缓解 |
|---|---|
| 节点级策略运行时切换导致数据不一致 | UI 灰显运行中节点的策略菜单 |
| ChildSession 在 OpenCode 后端权限继承行为不明 | 实施前 Phase C 期间压测 |
| InSessionSubagent 与父 chat 流竞争 | composer 禁用 |
| 自我调节产生死循环 | 每节点 max 1 次 + 全图 max 5 次 |
| Chat 流被 TaskGraph 输出淹没 | 节点运行中输出只走 panel,完成后汇总 |
| 多 panel 同时挂载 | 单例 ActiveGraph,新触发替换旧 |
| 大图(>50 节点)实时 panel 性能 | 复用 v1 bulk-replace 优化 + 节点 canvas 化简版 |
| Avalonia 12 自绘边性能 | 用 Shape / Path 而非每帧重画,数据驱动 RenderTransform |
| 现有 v1/v2 单元测试与 v3 模型字段扩展不兼容 | JsonTaskGraphStore 反序列化容错 + 测试套件覆盖 |

---

## 9. 技术债清单(后续阶段处理)

| 债项 | 原因 | 后续 |
|---|---|---|
| 节点级策略 UI 提示气泡 | v3 默认 AI 选 | v3.1 |
| 自我调节可视化编辑器 | v3 只允许 AI 改 | v3.2 |
| 多级嵌套 subagent (>2 层) | v3 仅 2 层 | v4 |
| 跨机器暂停-迁移-恢复 | v3 单机 | v4 |
| OpenCode Plugin 集成 | v3 不做 | v4 |
| Chat 历史自动归档 | v3 用 panel 隔离 | v3.1 |
| 策略学习 | v3 规则 | v4 |
| BugListInput 智能解析质量 | v3 规则,LLM 后续接 | v3.1 |

---

## 10. 文档交付清单

| 时机 | 文档 | 路径 |
|---|---|---|
| 已完成 | v1 方案 | `Docs/working/TaskGraph-方案.md` |
| 已完成 | v1 改造计划 | `Docs/working/TaskGraph-改造计划.md` |
| 已完成 | v2 编排方案 | `Docs/working/TaskGraph-编排方案.md` |
| **本阶段(已完成)** | **v3 委托与对话内编排方案** | **`Docs/working/TaskGraph-委托与对话内编排-方案.md`** |
| **本阶段(已完成)** | **v3 改造计划** | **`Docs/working/TaskGraph-委托与对话内编排-改造计划.md`(本文件)** |
| Phase G 完成后 | AGENTS.md 更新 | 增加 "v3 委托策略" + "对话内 TaskGraph" 章节 |
| Phase G 完成后 | architecture.md 更新 | §UI / §Layered Services 补充 v3 |

---

## 11. 变更日志

| 日期 | 版本 | 变更 |
|---|---|---|
| 2026-06-16 | v1.0 | 画图工具 + 节点编辑 + 持久化 |
| 2026-06-19 | v2.0 | 三种输入模式 + 节点执行 + 弹窗 |
| 2026-06-21 | v3.0 | 节点级委托策略 + 对话内 TaskGraph + 实时 UI |
