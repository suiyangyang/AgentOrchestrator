# TaskGraph 编排方案 (v2)

> 状态: 方案待评审
> 决策日期: 2026-06-19
> 范围: TaskGraph v2 增量——三种输入模式(智能 / 文档 / 直接)+ 真实 LLM 接入 + 节点执行 + 节点详情弹窗 + 持久化升级
> 配套文档:
> - [`TaskGraph-方案.md`](./TaskGraph-方案.md) (v1,已锁定,2026-06-16):画图工具 + 节点编辑 + 自动布局 + 规则 + Mock LLM 生成 + JSON 持久化
> - [`TaskGraph-改造计划.md`](./TaskGraph-改造计划.md) (v1,已锁定):v1 实施计划
> - 本文档 (v2):在 v1 之上叠加**输入模式 + 执行 + 持久化升级**

---

## 0. 与 v1 的关系与影响

### 0.1 关系声明

- **v1 的画图能力**(节点编辑、连线、自动布局、Inspector、ImportDialog、JSON 持久化)**继续作为后续实施基础,不被本方案覆盖**。
- **本方案新增能力**:三种输入模式、真实 LLM 接入、节点执行、节点详情弹窗、产物注入、执行期持久化。
- **命名风格**:本方案**继承 v1 的命名约定**(`TaskGraph` / `TaskNode` / `TaskEdge` / `TaskNodeKind` / `TaskNodeStatus`),仅在 v2 新增概念上引入新名称,不另起新词。
- **可执行性边界**:v2 的"执行"完全在应用层,通过 `IAgentGateway.CreateSessionAsync` + `SendMessageAsync` 完成,**不修改 OpenCode 后端协议**。

### 0.2 对 v1 已锁定设计的影响

| v1 锁定项 | v2 影响 |
|---|---|
| `TaskGraph` 是图数据 + 渲染模型 | 扩展为 v2 的**执行载体**,新增 `Mode` / `ExecutionState` 字段 |
| `TaskNode` 是编辑模型 | 扩展为**可执行节点**,新增 `AgentSessionId` / `AttemptCount` / `OutputSummary` / `LastError` 等字段 |
| `TaskNodeKind { Plan, Execute, Verify, Decision, Parallel, HumanInput }` | **完全复用**。v2 的 `Plan` Kind 用于模式 ① ② LLM 产出 |
| `TaskNodeStatus { Pending, Running, Completed, Failed, Skipped }` | **完全复用**。v2 的执行管道按此状态机推进 |
| `ITaskGraphLlmGateway` + `MockTaskGraphLlmGateway` | v2 替换为走 `IAgentGateway` 的生产实现(`LlmPlanner`) |
| `ITaskGraphStore` + `JsonTaskGraphStore`(JSON) | v2 扩展 schema,新增执行状态/产物/会话 ID 字段,**向前兼容** |
| v1 明确"**不做节点执行**" | v2 把执行作为核心能力补齐 |
| v1 明确"**走 Mock LLM**" | v2 切真实 LLM(走 `IAgentGateway` → OpenCode 后端) |
| v1 明确"**无 autosave**" | v2 加**执行期间**的自动状态持久化(节点状态变化即写入),编辑期仍手动 Save |
| `ITaskGraphGenerator` + `IChatTaskExtractor`(规则 + LLM) | v2 复用规则路径,但 LLM 路径由 `LlmPlanner` 接管 |

---

## 1. 目标与范围

### 1.1 目标

让用户能通过三种输入模式产生可执行任务编排,并让编排**实际运行起来**——包括按 DAG 依赖调度、隔离上下文、按节点查看流式执行详情。

> **边界说明**
>
> 本方案 v2 的能力定位是:先交付**可执行的静态 DAG 编排**与可恢复执行底座,再在后续阶段补齐运行期分支、暂停恢复、运行期扩图等**动态编排能力**。
>
> 因此:
> - **MVP / 阶段 2 / 阶段 3**:支持"预先成图 + 执行"的静态编排
> - **阶段 4+ / v2.1**:补齐部分动态编排语义
> - 当前文档中提到的"动态任务编排"仅指**演进方向**,不是 MVP 已承诺能力

### 1.2 三种输入模式

| 模式 | 输入 | Plan 来源 | LLM 调用 | MVP 范围 |
|---|---|---|---|---|
| ① 智能编排 | 一句自然语言(如"做一个 todo app") | LLM 解析为 JSON | 是(走 `IAgentGateway`) | 否,阶段 2 |
| ② 文档编排 | 拖入 MD / TXT 文件 | 文件内容 + LLM 提炼 | 是 | 否,阶段 3 |
| ③ 直接输入 | 列表文本(`- [ ]` / Markdown 列表 / JSON) | 本地解析 | **否** | **是,MVP** |

**前两种共享 Planner**,差异仅在 system prompt 与输入附件。

### 1.3 v2 核心能力

1. 三种输入模式(模式 ③ → ① → ② 递进交付)
2. LLM JSON 解析 + 失败重试 3 次 fallback
3. **每个节点独立 Agent Session**(上下文隔离)
4. 节点间**产物注入**(结构化摘要 + 文本摘要,长链降级 AI 摘要)
5. **DAG 依赖调度**(MVP 顺序 → 阶段 4 同层并行)
6. **节点详情弹窗**(复用 Chat 流式输出控件)
7. **Plan 持久化**(基于 v1 `ITaskGraphStore` 扩展)
8. **节点级失败重试 3 次 + DAG 拓扑自动跳过下游**

### 1.4 v2 显式不做

- LLM tool-use / 结构化输出契约(MVP 用 prompt + JSON 解析,后续可升级)
- 通用运行期新增节点 / 通用运行期扩图(dynamic expansion)
- `Decision` 节点的条件求值引擎
- `HumanInput` 节点的暂停-恢复闭环
- 跨 Plan 引用 / 模板库
- Plan 版本对比与合并
- 节点产物 diff 可视化(阶段 4 之后)
- PDF / DOCX 文档支持(阶段 4 之后)
- 撤销/重做(沿用 v1 决策,不做)

---

## 2. 架构总览

### 2.1 分层架构

```text
┌─────────────────────────────────────────────────────────────────┐
│  UI Layer                                                       │
│  TaskGraphWorkspaceControl + TaskGraphImportDialog             │
│  + PlanNodeDetailDialog (弹窗,复用 ChatWorkspaceControl)        │
└────────────────────────────┬────────────────────────────────────┘
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│  ViewModel Layer                                                │
│  TaskGraphWorkspaceViewModel (替换占位)                         │
│  PlanNodeDetailViewModel (弹窗 VM,持 AgentSessionId)            │
└────────────────────────────┬────────────────────────────────────┘
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│  Application Services                                           │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ Input Adapters (3 个,差异仅在"如何产出 Plan")           │    │
│  │  IntentAdapter   DocumentAdapter   DirectAdapter       │    │
│  └─────────────────────┬───────────────────────────────────┘    │
│                        ▼                                        │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ Planner Service (模式 ① ② 需要,模式 ③ 跳过)           │    │
│  │  IPlanner                                          │    │
│  │  ├─ LlmPlanner (JSON prompt + 3 次重试)            │    │
│  │  └─ DirectPlanner (本地规则,无 LLM)                │    │
│  └─────────────────────┬───────────────────────────────────┘    │
│                        ▼                                        │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ Orchestrator Service                                    │    │
│  │  IOrchestratorExecutor                                 │    │
│  │  ├─ DagExecutor (DAG 拓扑排序 + 顺序/同层并行调度)   │    │
│  │  └─ NodeOutputInjector (上游产物 → 下游输入)          │    │
│  └─────────────────────┬───────────────────────────────────┘    │
└────────────────────────┼────────────────────────────────────────┘
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│  Cross-cutting                                                  │
│  IAgentGateway (现有,OpenCode 后端)                            │
│  ITaskGraphStore (v1 已有,扩展 schema)                         │
│  IDocumentReader (MD / TXT,新增)                               │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 三种模式差异点

```text
模式 ① 智能编排:
  IntentAdapter
    → 用户文本
    → LlmPlanner.Prompt(system=plannerSystemPrompt, user=userText)
    → LLM 返回 raw text
    → JsonParsingService.ParseWithRetry(raw, schema, maxRetry=3)
    → OrchestrationPlan

模式 ② 文档编排:
  DocumentAdapter
    → IDocumentReader.Read(filePath)
      ├─ MarkdownReader (复用 Markdig 解析 heading/list/code fence)
      └─ TextReader (UTF-8 读全文)
    → LlmPlanner.Prompt(system=plannerSystemPrompt+documentHint, user=docContent)
    → JsonParsingService.ParseWithRetry(raw, schema, maxRetry=3)
    → OrchestrationPlan

模式 ③ 直接输入:
  DirectAdapter
    → 用户列表文本
    → DirectPlanner.Parse(text)
      ├─ 命中 JSON → 反序列化
      ├─ 命中 Markdown 列表 → 解析为节点
      └─ 命中 "depends on" 关键词 → 解析依赖
    → OrchestrationPlan (无 LLM 调用)
```

### 2.3 与现有架构契合

- 复用 MVVM + CommunityToolkit.Mvvm + CompiledBindings(沿用项目 Non-Negotiables)
- 复用 `IAgentGateway` / `OpenCodeAgentGateway`(节点执行)
- 复用 `ChatWorkspaceControl` / `ChatBlockControl`(弹窗内嵌)
- 复用 `ITaskGraphStore` / `JsonTaskGraphStore`(持久化)
- 复用 `SqliteSidebarRepository` 的仓储模式
- 复用 `DataPathProvider` 路径约定
- DI 注册沿用 v1 风格,加新条目

---

## 3. 数据模型

### 3.1 枚举(v2 新增 / v1 复用)

```csharp
// ── v1 已锁定,v2 复用 ─────────────────────────────────────
public enum TaskNodeKind    { Plan, Execute, Verify, Decision, Parallel, HumanInput }
public enum TaskNodeStatus  { Pending, Running, Completed, Failed, Skipped }
public enum TaskGraphSourceKind { File, InlineText, ChatMessages }

// ── v2 新增 ───────────────────────────────────────────────
public enum TaskGraphMode   { Intent, Document, Direct }
// v2 新增,描述 Plan 的产生方式

public enum TaskGraphExecutionState { Draft, Running, Completed, Failed, Cancelled }
// v2 新增,描述整个 Plan 的执行状态
// Draft:     刚生成,未开始执行
// Running:   至少有一个节点 Running
// Completed: 所有节点 Completed 或 Skipped
// Failed:    至少一个节点 Failed 且无法继续
// Cancelled: 用户主动取消
```

### 3.1.1 节点状态机约束

为避免实现时混用 `Completed` / `Succeeded` / `Cancelled`,v2 明确采用下列规则:

- `TaskNodeStatus` 仅使用:
  - `Pending`
  - `Running`
  - `Completed`
  - `Failed`
  - `Skipped`
- **不新增 `Succeeded`**
  - 文档中所有 "`Succeeded`" 统一指 `Completed`
- **不新增节点级 `Cancelled`**
  - 用户取消执行时:
    - 当前 `Running` 节点允许自然结束后标记为 `Completed` 或 `Failed`
    - 尚未开始的 `Pending` 下游节点统一标记为 `Skipped`
    - 整个 `TaskGraph.ExecutionState` 标记为 `Cancelled`

这样做的原因:
- v1 已锁定 `TaskNodeStatus`,v2 不扩枚举可避免 UI / 序列化 / 颜色映射连锁修改
- 取消是**图级控制语义**,不是节点完成语义

### 3.2 TaskGraph 扩展(v1 模型 + v2 字段)

```csharp
// ── v1 已锁定 ─────────────────────────────────────────────
public sealed partial class TaskGraph : ObservableObject
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "未命名图";
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ObservableCollection<TaskNode> Nodes { get; init; } = [];
    public ObservableCollection<TaskEdge> Edges { get; init; } = [];
    public void RebuildEdges() { /* 从 Nodes.DependsOn 重建 Edges */ }
}

// ── v2 在 v1 基础上扩展 ──────────────────────────────────
public sealed partial class TaskGraph : ObservableObject
{
    // ... v1 字段保留 ...

    // v2 新增:
    [ObservableProperty]
    private TaskGraphMode _mode = TaskGraphMode.Direct;

    [ObservableProperty]
    private TaskGraphExecutionState _executionState = TaskGraphExecutionState.Draft;

    [ObservableProperty]
    private string? _sourceContent;        // 模式 ① ② 保留原始输入,用于重试

    [ObservableProperty]
    private string? _sourceFilePath;       // 模式 ② 保留文档路径,用于 reload

    [ObservableProperty]
    private DateTimeOffset? _executionStartedAt;

    [ObservableProperty]
    private DateTimeOffset? _executionCompletedAt;
}
```

### 3.3 TaskNode 扩展(v1 模型 + v2 执行相关字段)

```csharp
// ── v1 已锁定 ─────────────────────────────────────────────
public sealed partial class TaskNode : ObservableObject
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "新任务";
    public string Description { get; set; } = string.Empty;
    public TaskNodeKind Kind { get; set; } = TaskNodeKind.Execute;
    public TaskNodeStatus Status { get; set; } = TaskNodeStatus.Pending;
    public string? AgentHint { get; set; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public NodePosition Position { get; set; } = new(40, 40);
    public ObservableCollection<string> DependsOn { get; init; } = [];
}

// ── v2 在 v1 基础上扩展 ──────────────────────────────────
public sealed partial class TaskNode : ObservableObject
{
    // ... v1 字段保留 ...

    // v2 新增 — LLM 调度相关:
    [ObservableProperty]
    private string _prompt = string.Empty;
    // 给 Agent Session 用的 prompt(模式 ① ② 由 Planner 填充,
    // 模式 ③ 由用户写 Description 自动组装)

    // v2 新增 — 执行相关:
    [ObservableProperty]
    private string? _agentSessionId;
    // 执行时回填,弹窗查看流式输出靠这个

    [ObservableProperty]
    private int _attemptCount;
    // 当前重试次数,达到上限(默认 3)标记 Failed

    [ObservableProperty]
    private string? _lastError;
    // 失败原因,UI 展示用

    [ObservableProperty]
    private string? _outputSummary;
    // 注入给下游节点的产物摘要(本地生成或 AI 摘要)

    [ObservableProperty]
    private DateTimeOffset? _startedAt;

    [ObservableProperty]
    private DateTimeOffset? _completedAt;

    // v2 派生计算:
    public bool CanRetry => Status == TaskNodeStatus.Failed && AttemptCount < 3;
    public bool IsExecutable => Kind != TaskNodeKind.HumanInput;
}
```

### 3.4 OrchestrationPlan 概念说明

`TaskGraph` 在 v1 是"图 + 节点 + 边"的容器。v2 把它**同时作为 OrchestrationPlan**——这是有意为之,因为 v1 已经定义了完整的图数据结构,新增"Plan"概念意味着再加一层抽象,徒增映射成本。

如果后续需要"Plan ≠ Graph"的场景(例如 Plan 是更高级的"项目",包含多个 Graph),再拆分。当前 v2 范围内:`TaskGraph == Plan`。

### 3.4.1 动态编排能力分级

为避免后续讨论中把"静态 DAG 执行"与"动态编排"混为一谈,这里锁定能力分级:

| 级别 | 能力 | v2 覆盖 |
|---|---|---|
| L1 | 预先生成 DAG,按依赖执行 | **是** |
| L2 | 同层并行、失败重试、恢复状态 | **是**(并行在阶段 4) |
| L3 | 运行时条件分支(`Decision`) | **否**,仅保留节点类型 |
| L4 | 人工介入后恢复(`HumanInput`) | **否**,仅保留节点类型 |
| L5 | 运行期新增节点 / 扩图 | **部分支持**:仅复杂功能开发模板的计划节点可向当前图注入执行节点 |

因此,`TaskGraph v2` 当前应准确表述为:

> 一个面向 Agent 任务执行的、可持久化的、可恢复的 **DAG orchestration runtime**。

而不是完整的动态工作流引擎。

### 3.5 模型文件清单

| 路径 | 状态 | 说明 |
|---|---|---|
| `Models/TaskGraph/TaskGraph.cs` | **修改** | 加 v2 字段 |
| `Models/TaskGraph/TaskNode.cs` | **修改** | 加 v2 字段 |
| `Models/TaskGraph/TaskGraphMode.cs` | **新增** | enum |
| `Models/TaskGraph/TaskGraphExecutionState.cs` | **新增** | enum |
| `Models/TaskGraph/PlannerSchema.cs` | **新增** | LLM 输出 JSON 的 C# 镜像(供反序列化) |

---

## 4. 三种输入模式

### 4.1 模式 ③:直接输入(MVP)

无 LLM 调用,纯本地规则解析。复用 v1 的 `ITaskGraphGenerator` + `RuleBasedExtractor`,逻辑完全一致。

**入口**:TaskGraph 工具栏 `[Import]` → 粘贴列表文本 → 采纳。

**支持的输入格式**(沿用 v1 规则):

```markdown
- 需求分析
- 架构设计
- 编码实现
  depends on: 架构设计
- 测试验证
  depends on: 编码实现
```

或 JSON:

```json
{
  "nodes": [
    {"id": "n1", "title": "需求分析"},
    {"id": "n2", "title": "架构设计"},
    {"id": "n3", "title": "编码实现", "dependsOn": ["n2"]}
  ]
}
```

### 4.2 模式 ①:智能编排(阶段 2)

**入口**:TaskGraph 工作区"意图输入框" → 用户写自然语言 → 点 `[编排]` → LLM 解析。

**流程**:

```text
用户输入: "我想做一个 todo app,需要支持增删改查"
        ↓
LlmPlanner.PromptAsync(userText)
        ↓
  systemPrompt = PLANNER_SYSTEM_PROMPT  // 见 4.4 节
  userContent  = userText
        ↓
  IAgentGateway.SendMessageAsync(plannerSessionId, request)
  // 复用现有 ChatSendAsync,但不写 Chat UI,只取最终文本
        ↓
  LLM 返回 raw text(预期是 JSON)
        ↓
JsonParsingService.ParseWithRetry(raw, PlannerSchema, maxRetry=3)
  // 重试机制见第 5 节
        ↓
PlannerSchema → OrchestrationPlan (即 TaskGraph)
```

**Planner Session** 是一个**专用会话**,不计入用户 Chat 历史:

- 每次模式 ① 触发,`CreateSessionAsync(title: "Planner-{timestamp}")`
- 不显示在侧边栏(用一个 internal flag 标记)
- session 消息只用于重试上下文,执行完丢弃(可选保留 7 天用于 debug)

### 4.3 模式 ②:文档编排(阶段 3)

**入口**:TaskGraph 工作区"拖入文件"区域(或工具栏 `[从文档编排]`)。

**支持的格式**:Markdown(`.md`)、纯文本(`.txt`)。

**流程**:

```text
用户拖入 spec.md
        ↓
IDocumentReader.Read(spec.md)
  ├─ MarkdownReader:用 Markdig 解析,提取 heading/list/code fence
  └─ TextReader:UTF-8 全文
        ↓
LlmPlanner.PromptAsync(userText="", documentContent=content)
        ↓
  systemPrompt = PLANNER_SYSTEM_PROMPT + DOCUMENT_HINT
  userContent  = "请基于以下文档生成任务编排:\n\n" + content
        ↓
  (后续同模式 ①)
```

**ChatAttachment / prompt part 现状校正**(实施前需 verify):

- App 层现有 `ChatAttachment` 的发送逻辑**只处理图片附件**
- `OpenCode.Client` 底层已经具备 `text` / `file` part 输入模型
- 因此阶段 3 的主要工作不是"后端是否支持文本",而是:
  1. 决定文档内容是**直接拼入 prompt**
  2. 还是通过 App 层补一个"文本文件 / 文本片段"附件映射

**锁定建议**:
- **优先方案**:阶段 3 先直接把文档内容拼入 planner prompt,不改 `ChatAttachment`
- **扩展方案**:若后续需要保留行号/来源范围,再补文本附件映射

**实施前置 verify 任务**(阶段 3 启动前):
1. 查 `OpenCode.Client/Requests/SessionRequests.cs`(prompt 接受什么 part 类型)
2. 查 `OpenCode.Client/Models/PartInput.cs` / `FilePartSource.cs`
3. 决定阶段 3 采用"直接拼 prompt"还是"扩展 App 层附件映射"

### 4.4 Planner System Prompt(初版,实施时可调)

```text
You are a task planner. Given a user's request, decompose it into a
directed acyclic graph (DAG) of executable tasks.

Output MUST be a single JSON object with this exact schema:
{
  "title": "<plan title>",
  "nodes": [
    {
      "id": "<short stable id, e.g. 'n1', 'n2'>",
      "title": "<task title, max 50 chars>",
      "description": "<what this task does, 1-3 sentences>",
      "kind": "Execute" | "Plan" | "Verify" | "Decision",
      "dependsOn": ["<id of prerequisite task>", ...]
    }
  ]
}

Rules:
- Each task must be independently executable (one concrete outcome).
- Use dependsOn ONLY for true prerequisites. Do not chain tasks that
  could run in parallel.
- If the user request is vague, make reasonable assumptions and document
  them in the task descriptions.
- Output JSON ONLY. No markdown fences, no preamble, no explanation.
```

### 4.5 PlannerSchema(LLM JSON 输出的 C# 镜像)

```csharp
public sealed record PlannerSchema
{
    public string Title { get; init; } = string.Empty;
    public IReadOnlyList<PlannerNodeSchema> Nodes { get; init; } = [];
}

public sealed record PlannerNodeSchema
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Kind { get; init; } = "Execute";  // 解析后映射到 TaskNodeKind
    public IReadOnlyList<string> DependsOn { get; init; } = [];
}
```

---

## 5. LLM JSON 解析重试策略

### 5.1 解析流程

封装在 `JsonParsingService.ParseWithRetry<T>(rawText, maxRetry=3)`:

```text
attempt 1:
  ParseRawJson(rawText)
  ├─ 成功 → 返回 PlannerSchema
  └─ 失败 → 进入 attempt 2

attempt 2:
  把上一次 raw 输出 + 错误信息拼成新的 user prompt:
    "以下是你上一次的输出:\n<rawText>\n\n错误:<errMsg>\n\n请修正为严格 JSON。"
  重新调 LLM,再尝试 ParseRawJson
  └─ 失败 → 进入 attempt 3

attempt 3:
  同 attempt 2,但 system prompt 加重 + 给完整 schema 示例
  └─ 仍失败 → 抛 PlannerParseException
```

### 5.2 ParseRawJson 实现要点

```csharp
private static PlannerSchema? ParseRawJson(string raw)
{
    // 策略 1: 直接 Parse
    try { return JsonSerializer.Deserialize<PlannerSchema>(raw, _opts); }
    catch { }

    // 策略 2: 提取 markdown fence ```json ... ``` 中的内容
    var match = Regex.Match(raw, @"```(?:json)?\s*(\{[\s\S]*?\})\s*```");
    if (match.Success)
    {
        try { return JsonSerializer.Deserialize<PlannerSchema>(match.Groups[1].Value, _opts); }
        catch { }
    }

    // 策略 3: 找第一个 { 到最后一个 } 的区间,尝试解析
    var start = raw.IndexOf('{');
    var end = raw.LastIndexOf('}');
    if (start >= 0 && end > start)
    {
        try { return JsonSerializer.Deserialize<PlannerSchema>(raw[start..(end+1)], _opts); }
        catch { }
    }

    return null;
}
```

### 5.3 失败兜底

3 次重试后仍失败:
- 抛 `PlannerParseException(message, rawText)`
- UI 在 ImportDialog 显示**"无法解析 LLM 输出,已重试 3 次"**
- 提供两个按钮:
  - `[手动修正]` → 打开文本编辑器,显示 raw 输出,用户改完再点"再试"
  - `[取消]` → 关闭对话框,TaskGraph 工作区回到空状态

---

## 6. 节点执行

### 6.1 Session 模型决策:每节点独立 Session

**决策**:每个 Node 一个独立 Agent Session。

**理由**(对比共享 Session):

| 维度 | 独立 Session | 共享 Session |
|---|---|---|
| 上下文隔离 | ✅ 完全隔离 | ❌ 长链路必污染 |
| 跨节点信息 | 需显式注入产物 | 自动继承 |
| 并行执行 | ✅ 天然支持 | ❌ 物理不可能 |
| 失败重试 | 单节点重跑 | 整链从断点续 |
| 上下文长度 | 各自独立,无累积 | 累积,易超限 |
| 与 `IAgentGateway` 契合 | 完全契合(CreateSessionAsync 即用) | 与现有 Chat 模式一致 |

**结论**:DAG + 依赖已锁定,独立 Session 是 v2 最合适的路线。

补充说明:
- 这并不自动等价于"具备恢复执行"
- 真正可恢复还需要定义**resume 协议**、**重复执行保护**、**运行中节点重建策略**

### 6.2 执行流程

```text
DagExecutor.ExecuteAsync(TaskGraph graph)
        ↓
1. 拓扑排序 graph.Nodes(按 DependsOn)
   ├─ 检测循环依赖 → 抛 InvalidPlanException
   └─ 顺序层序化(无依赖的节点同层)
        ↓
2. 按层顺序执行:
   层 0: [A, B, C]  // 无依赖
   层 1: [D, E]     // 依赖层 0
   层 2: [F]        // 依赖层 1
        ↓
3. 对每层内的节点:
   MVP: 顺序执行(MVP 阶段只做这个)
   阶段 4: 同层并行执行
        ↓
4. 对每个节点 Node:
   a. Status = Running;StartedAt = now
   b. await Persist(graph)  // 即时持久化
   c. var sessionId = await agent.CreateSessionAsync(
        new SessionCreateRequest(
          WorkingDirectory = currentProjectDir,
          Title = $"TaskGraph-{graph.Id}-{node.Id}"))
   d. Node.AgentSessionId = sessionId
   e. var prompt = NodeOutputInjector.InjectUpstreamOutputs(node, graph)
      // 把上游节点的 OutputSummary 拼进 prompt
   f. await foreach chunk in agent.SendMessageAsync(sessionId,
        new ChatRequest(prompt, [], "allow", model))
      // 监听流式,弹窗订阅同一 session 可看实时进度
   g. 收到 session.idle 事件 → Status = Completed;CompletedAt = now
      ├─ 调用 NodeOutputInjector.GenerateSummary(node, sessionId)
      │   // 拉 messages 摘要,生成 OutputSummary
      └─ await Persist(graph)
   h. 异常 → 进入重试逻辑(见 6.4)
        ↓
5. 全部节点完成:
   graph.ExecutionState = Completed;ExecutionCompletedAt = now
   await Persist(graph)
```

### 6.2.1 恢复执行协议(新增,锁定)

为让"执行期持久化"真正可用,v2 补充恢复协议:

1. **应用启动 / 打开 Plan 时做一次 reconcile**
   - 若 `graph.ExecutionState != Running`,按持久化状态展示即可
   - 若 `graph.ExecutionState == Running`,进入恢复检查

2. **恢复检查规则**
   - `Pending` 节点:保持 `Pending`
   - `Completed / Failed / Skipped` 节点:视为终态,不重跑
   - `Running` 节点:
     - 若 `AgentSessionId` 为空:标记 `Failed`(`LastError = "执行中断:缺少 session id"`)
     - 若 `AgentSessionId` 不为空:
       - 尝试 `GetMessagesAsync(sessionId)` 拉历史
       - 若能拉到历史:
         - 该节点转为 `Failed`,并记录 `LastError = "应用中断,需手动重试"`
         - **不自动重放 prompt**
       - 若 session 已不存在或网关报错:
         - 同样转 `Failed`

3. **为什么不自动续跑**
   - 当前 `IAgentGateway` 没有"attach 到既有流并判断是否仍在执行"的稳定协议
   - 自动重放 prompt 可能造成重复副作用(重复改文件、重复提交)
   - 因此 v2 锁定为:**恢复展示状态可以自动,恢复执行必须用户显式点击重试**

4. **重试语义**
   - 手动重试失败节点时:
     - 清空 `LastError`
     - 重新创建 session
     - 重新组装 prompt
     - 仅重跑该节点及其仍为 `Skipped` 的可达下游

这是 v2 的"可恢复执行"边界:**可恢复到一致状态,但不承诺自动无损续跑**

### 6.3 产物注入(NodeOutputInjector)

**注入策略**(决策点 B 的回选):

- **默认**:结构化摘要 + 文本摘要双通道
  ```csharp
  var sb = new StringBuilder();
  sb.AppendLine("Upstream task outputs:");
  foreach (var dep in node.DependsOn)
  {
      var depNode = graph.Nodes.First(n => n.Id == dep);
      sb.AppendLine($"- {depNode.Title}: {depNode.OutputSummary}");
  }
  sb.AppendLine();
  sb.AppendLine("---");
  sb.AppendLine();
  sb.AppendLine(node.Prompt);
  return sb.ToString();
  ```
- `OutputSummary` 在 v2 中至少包含两部分:
  - 面向 LLM 的文本摘要
  - 面向运行时的结构化元数据(例如涉及文件路径列表、关键结果标签)
- **MVP 简化**:结构化元数据先做最小集:
  - `TouchedFiles: string[]`
  - `ResultTags: string[]`
  - `SummaryText: string`
- **长链降级(>5 节点)**:由 LLM 摘要上游 `SummaryText` 后再注入(避免 prompt 超长)
- **未来可扩展**:显式文件 diff、git status 等

### 6.4 失败处理与重试

```text
节点执行异常:
  attempt 1 失败 → AttemptCount++
    ├─ AttemptCount < 3 → 重试(同一 sessionId 不复用,新建 session)
    └─ AttemptCount >= 3 → Status = Failed;LastError = ex.Message

Status = Failed:
  ├─ DAG 拓扑:该节点的所有下游节点 Status = Skipped
  └─ graph.ExecutionState = Failed
     ├─ 若至少有一个顶层节点 Completed → UI 显示"部分失败"
     └─ 若全部失败 → UI 显示"完全失败"
```

上面旧文案中的 `Succeeded` 在实现中统一按 `Completed` 解释。

**重试**:UI 工具栏 `[重试失败节点]` 按钮,允许用户手动重试失败的节点(重置 AttemptCount)。

**取消**:UI 工具栏 `[取消执行]` 按钮:

- 不再接纳新的 `Pending` 节点进入执行
- 尚未开始的 `Pending` 节点统一标记 `Skipped`
- 已在执行中的 `Running` 节点不强行改节点状态,等待其自然结束
- `graph.ExecutionState = Cancelled`

> 备注:若后续 `IAgentGateway` 提供稳定的 session cancel/interrupt 能力,再升级为真正中断运行中节点。

### 6.5 节点弹窗(PlanNodeDetailDialog)

点击任意 Node → 弹出 `PlanNodeDetailDialog`,内容布局:

```text
┌─ 节点详情 ──────────────────────────────────────────────┐
│  [x] 关闭                  节点 #2: 添加登录页          │
├─────────────────────────────────────────────────────────┤
│  ┌─────────────────────────────────────────────────┐    │
│  │  嵌入 ChatWorkspaceControl(或复用 ChatBlockControl)│    │
│  │  ───────────────────────────────────────────────│    │
│  │  [Agent Session 流式输出]                       │    │
│  │  [TextBlock / ThoughtBlock / ToolBlock / Task]  │    │
│  │  [实时更新,直到 Session 状态 = Idle]            │    │
│  └─────────────────────────────────────────────────┘    │
│  [取消重试]  [查看文件改动 diff]  [复制 session id]     │
└─────────────────────────────────────────────────────────┘
```

**实现要点**:
- 弹窗 VM 持有 `AgentSessionId`(从 `TaskNode.AgentSessionId` 回填)
- 加载时 `await IAgentGateway.GetMessagesAsync(sessionId)` 拉历史
- 弹窗订阅 SSE 流式事件,实时追加新 chunk
- **完全复用** Chat 的 `ChatWorkspaceControl` 或 `ChatBlockControl`,不需要新控件
- `diff` 按钮:
  - **当前现状校正**:`IAgentGateway` 现有接口**没有** `DiffAsync`
  - v2 MVP 不把 diff 作为已锁定能力
  - 阶段 4 若要支持,有两条路:
    1. 给 `IAgentGateway` 新增 `DiffAsync`
    2. 由专门的 session-diff service 直接包装 OpenCode client

**锁定建议**:
- MVP 弹窗先保留 `[复制 session id]`
- `[查看文件改动 diff]` 按钮降级为阶段 4 待定项

---

## 7. 持久化

### 7.1 v1 基础 + v2 扩展

v1 的 `ITaskGraphStore` + `JsonTaskGraphStore` 已实现 JSON 持久化。v2 扩展 schema:

```json
{
  "id": "abc123",
  "name": "Todo App 编排",
  "createdAt": "2026-06-19T10:00:00Z",
  "updatedAt": "2026-06-19T10:30:00Z",
  "mode": "Intent",                           // v2 新增
  "executionState": "Running",                // v2 新增
  "executionStartedAt": "2026-06-19T10:15:00Z",
  "executionCompletedAt": null,
  "sourceContent": "我想做一个 todo app...",   // v2 新增
  "sourceFilePath": null,
  "nodes": [
    {
      "id": "n1",
      "title": "需求分析",
      "description": "...",
      "kind": "Plan",
      "status": "Completed",
      "agentHint": null,
      "tags": [],
      "position": {"x": 40, "y": 40},
      "dependsOn": [],
      "prompt": "分析需求并产出文档",            // v2 新增
      "agentSessionId": "ses_xxx",             // v2 新增
      "attemptCount": 0,                       // v2 新增
      "lastError": null,                       // v2 新增
      "outputSummary": "需求: 增删改查",        // v2 新增
      "startedAt": "2026-06-19T10:15:00Z",     // v2 新增
      "completedAt": "2026-06-19T10:18:00Z"    // v2 新增
    }
  ],
  "edges": [...]
}
```

### 7.2 向后兼容

`JsonTaskGraphStore` 在反序列化时:
- 缺失字段 → 用默认值填充(`Mode = Direct`,`ExecutionState = Draft` 等)
- 多余字段 → 忽略
- 老 v1 文件 → 自动按 v1 行为加载(`Mode = Direct`,所有节点 `Status = Pending`)
- 写入时:始终按 v2 schema 写出

### 7.3 持久化时机

| 阶段 | 持久化行为 |
|---|---|
| 编辑期(节点增删改) | 沿用 v1:**手动 Save**,无 autosave |
| 执行期(节点状态变化) | **v2 新增自动持久化**:每次 `Status` 变化、AgentSessionId 回填、OutputSummary 生成都 `SaveAsync` |

### 7.3.1 幂等与重复执行边界

由于节点执行可能产生真实副作用(改文件、调用外部服务),v2 明确:

- 持久化的目标是**恢复一致状态**
- 不是保证节点天然幂等
- 执行器本身不负责推断"这个节点是否可以安全重放"

因此:
- 自动恢复时**不自动重发 prompt**
- 手动重试是显式用户行为
- 后续若需要更强保障,可在节点模型中增加:
  - `ExecutionFingerprint`
  - `SideEffectLevel`
  - `IsReplaySafe`

### 7.4 PlanRepository(可选抽象)

如果后续需要"按模式 / 按状态查询 Plan 列表",可以加 `IPlanRepository` 抽象。MVP 阶段**直接复用 `ITaskGraphStore`**,不引入新抽象。

---

## 8. 决策记录(锁定清单)

| 决策点 | 选择 | 备注 |
|---|---|---|
| 三种输入模式共享一个 Plan 模型 | **共享**(TaskGraph 同时作为 Plan) | 避免重复抽象 |
| LLM 调用走 `IAgentGateway` | **是** | 不新增 Provider 设置,统一走 OpenCode 后端 |
| 文档格式范围 | **MD / TXT** | PDF / DOCX 不做 |
| Session 模型 | **每节点独立 Session** | 上下文隔离 + 天然并行 |
| 节点产物注入形式 | **结构化摘要 + 文本摘要**,长链降级 AI 摘要 | 见 6.3 |
| 文档输入接入方式 | **优先直接拼 prompt**,必要时再扩展附件映射 | 见 4.3 |
| 并行执行时机 | **阶段 4**,MVP 只做顺序 | 避免 DAG 拓扑 + UI 并发同时搞 |
| 失败节点策略 | **节点级重试 3 次 + DAG 拓扑自动跳过下游** | 与 JSON 解析重试策略对齐 |
| 节点弹窗实现 | **复用 ChatWorkspaceControl / ChatBlockControl** | 不新建控件 |
| Plan 持久化 | **扩展 v1 ITaskGraphStore**,JSON schema 加 v2 字段 | 向前兼容 |
| 执行期持久化时机 | **节点状态变化即写入** | 编辑期仍手动 |
| 恢复策略 | **自动 reconcile,不自动续跑** | 避免重复副作用 |
| 三种输入模式实现路径 | 模式 ③ → ① → ② 递进交付 | MVP 仅 ③ |
| LLM JSON 重试策略 | **3 次 + 失败时弹 UI 让用户手动修正** | 见 5.3 |
| 模型命名 | **继承 v1**(`TaskGraph` / `TaskNode` / `TaskNodeStatus`) | 不另起新词 |
| v1 决策不变 | **沿用**(画图工具能力不被本方案覆盖) | 见 0.2 |

---

## 9. 实施阶段路线

### 9.1 阶段总览

| 阶段 | 范围 | 解决什么风险 | 估时 |
|---|---|---|---|
| **MVP (阶段 1)** | 模式 ③ 直接输入 + 节点执行(顺序) + 节点弹窗(复用 Chat) + 持久化扩展 + 节点级重试 | 跑通端到端、零 LLM 风险、验证 DAG 模型 + 执行管道 | 3.0d |
| **阶段 2** | 模式 ① 智能编排 + LlmPlanner + JsonParsingService(3 次重试) + Planner Session 不入侧边栏 | 核心差异化能力,验证 JSON 鲁棒性 | 1.5d |
| **阶段 3** | 模式 ② 文档编排 + IDocumentReader(MD/TXT) + ChatAttachment 扩展(若需要) | 增量小,基于阶段 2 | 1.0d |
| **阶段 4** | 同层并行执行 + 长链降级 AI 摘要注入 + diff 能力补齐 | 性能与长链路场景 | 2.0d |
| **阶段 5** | 恢复协议验证 + 文档 | 补齐恢复/取消主路径 + `AGENTS.md` 更新 | 0.5d |

**总计约 8 工作日**(单人串行)。如并行派 subagent 可压缩到 3-4 天。

### 9.2 MVP 阶段详细任务

> MVP 阶段目标是"零 LLM 风险、端到端验证整套架构"。

**子任务**:

| # | 任务 | 文件 |
|---|---|---|
| M1 | 扩展 `TaskGraph` 模型:加 `Mode` / `ExecutionState` / `SourceContent` / `SourceFilePath` / `ExecutionStartedAt` / `ExecutionCompletedAt` | `Models/TaskGraph/TaskGraph.cs` |
| M2 | 扩展 `TaskNode` 模型:加 `Prompt` / `AgentSessionId` / `AttemptCount` / `LastError` / `OutputSummary` / `StartedAt` / `CompletedAt` / `CanRetry` / `IsExecutable` | `Models/TaskGraph/TaskNode.cs` |
| M3 | 新增枚举 `TaskGraphMode` / `TaskGraphExecutionState` | `Models/TaskGraph/*.cs` |
| M4 | 新增 `IOrchestratorExecutor` + `DagExecutor`(MVP 顺序) | `Services/Orchestration/IOrchestratorExecutor.cs` / `DagExecutor.cs` |
| M5 | 新增 `INodeOutputInjector` + `DefaultNodeOutputInjector` | `Services/Orchestration/INodeOutputInjector.cs` |
| M6 | 替换 `TaskGraphWorkspaceViewModel`(占位 → 真):持有 Plan / Nodes / 当前选中 / 执行命令 / 重试命令 | `ViewModels/TaskGraphWorkspaceViewModel.cs` |
| M7 | 新增 `PlanNodeDetailViewModel` + `PlanNodeDetailDialog`(内嵌 ChatWorkspaceControl) | `ViewModels/` + `Controls/TaskGraph/PlanNodeDetailDialog.axaml(.cs)` |
| M8 | 扩展 `TaskGraphWorkspaceControl.axaml`:执行工具栏(执行 / 取消 / 重试失败节点)+ 节点状态颜色 | `Controls/TaskGraph/TaskGraphWorkspaceControl.axaml` |
| M9 | 扩展 `JsonTaskGraphStore` schema:解析时容错 v1 老文件,写出时按 v2 schema | `Services/TaskGraph/JsonTaskGraphStore.cs` |
| M10 | DI 注册新 service / VM | `App.axaml.cs` |
| M11 | 验证 5 条主路径(见 9.3) | — |

**MVP 验收**:
- `dotnet build` 干净,0 warning
- TaskGraph 工具栏 `[Import]` → 粘贴模式 ③ 的 markdown → 采纳 → 节点出现
- 点 `[执行]` → DAG 顺序执行(每节点一个 session)→ 状态实时更新
- 点击节点 → 弹窗显示流式输出(复用 Chat)
- 强制让某节点失败(可临时注入 throw)→ 重试 3 次 → 标 Failed → 下游 Skipped
- `[保存]` → 关闭应用 → 重新打开 → `[打开]` 该 Plan → 状态完全恢复

### 9.3 主路径验证矩阵

| 路径 | 操作 | 期望 |
|---|---|---|
| P1 | 手动建图 + 持久化往返 | 同 v1 |
| P2 | 模式 ③ markdown 导入 | 同 v1 |
| P3 | Chat 抽取 | 同 v1 |
| P4 | `/plan` 命令 | 同 v1 |
| P5 | Chat ↔ Graph 切换状态保持 | 同 v1 |
| **P6 (v2 MVP)** | 模式 ③ 导入 → 执行 → 节点状态实时更新 | 节点按 DAG 顺序 Running → Completed |
| **P7 (v2 MVP)** | 点击节点 → 弹窗显示流式输出 | 流式正常,关闭后能再次打开看到历史 |
| **P8 (v2 MVP)** | 节点失败重试 3 次 + 下游 Skipped | 状态正确,持久化能恢复 |
| **P8.1 (v2 MVP)** | 执行中关闭应用 → 重开 → 打开 Plan | `Running` 节点转 `Failed`,需用户显式重试 |
| **P9 (v2 阶段 2)** | 模式 ① 输入模糊文本 → LLM 解析 → 采纳 | 至少 1 次重试后成功 |
| **P10 (v2 阶段 2)** | LLM 返回无效 JSON → 3 次重试后失败 → UI 弹手动修正对话框 | 流程正确 |
| **P11 (v2 阶段 3)** | 模式 ② 拖入 MD → 编排 → 执行 | 同 P6 |
| **P12 (v2 阶段 4)** | 多节点同层 → 并行执行 | 同层节点并行 |

### 9.4 Subagent 派发策略

| 阶段 | 派发方式 |
|---|---|
| MVP(M1-M5 数据/服务层) | 3 个并行 `deep`:①TaskGraph 扩展、②TaskNode 扩展 + 枚举、③Executor + OutputInjector |
| MVP(M6-M10 VM/UI 层) | 2 个并行:`deep`(M6+M9+M10)+ `visual-engineering`(M7+M8) |
| 阶段 2 | 1 个 `deep`(LlmPlanner + JsonParsingService)+ 1 个 `visual-engineering`(意图输入框 UI) |
| 阶段 3 | 1 个 `deep`(IDocumentReader + ChatAttachment 扩展) |
| 阶段 4 | 1 个 `deep`(DagExecutor 并行)+ 1 个 `visual-engineering`(节点状态色条更新) |
| 阶段 5 | 主 orchestrator 自己跑验证 + 文档 |

---

## 10. 风险与缓解

| 风险 | 缓解措施 |
|---|---|
| LLM JSON 解析鲁棒性 | 3 次重试 + 手动修正兜底 + 严格 system prompt |
| Planner Session 污染用户 Chat 历史 | 用专用 `Planner-{timestamp}` Title + 标记为 internal(不显示在侧边栏) |
| 节点执行期间网络异常 | 节点级重试 3 次 + Status 持久化,断网重连后能恢复 |
| DAG 循环依赖 | `DagExecutor.ExecuteAsync` 入口处拓扑排序检测,异常抛 `InvalidPlanException` |
| v1 老 JSON 文件兼容 | `JsonTaskGraphStore` 反序列化时容错,缺失字段用默认值 |
| v2 字段太多导致 JSON 膨胀 | 单 Plan 通常 < 100 节点,JSON < 1MB,可接受 |
| 并行执行对 OpenCode 后端压力 | 阶段 4 实装前压测,必要时加并发上限(默认 max parallel = 4) |
| 节点详情弹窗复用 Chat 的耦合 | 弹窗通过 `IAgentGateway` 拉数据,不直接引用 ChatWorkspaceViewModel |
| 应用中断后误自动重跑 | 锁定为"只恢复状态,不自动续跑",由用户显式重试 |
| 自然语言摘要丢失关键上下文 | `OutputSummary` 升级为结构化摘要 + 文本摘要 |

---

## 11. 技术债清单(后续阶段处理)

| 债项 | 原因 | 后续 |
|---|---|---|
| LLM tool-use / 结构化输出契约 | MVP 用 prompt + JSON 解析,OpenCode 后端支持成熟后可升级 | v2.1 |
| Planner Session 永久保留 vs 7 天清理 | MVP 全部保留以简化实现 | v2.1 |
| PDF / DOCX 文档支持 | 阶段 3 锁定 MD/TXT | v2.2 |
| 节点产物 diff 可视化 | 需先补 gateway 或专用 diff service | v2.1 |
| 并行执行的 UI 状态合并 | MVP 只做顺序,阶段 4 才需要处理同层并发 UI 更新 | 阶段 4 |
| Plan 历史版本对比 | 用户可能想看 Plan 的演化 | v2.2+ |
| 跨 Plan 引用 / 模板库 | 不在 v2 范围 | v3+ |
| `Decision` / `HumanInput` 运行语义 | 当前仅有节点类型,无执行语义 | v2.1 |
| 运行期扩图(dynamic expansion) | 当前不支持 | v2.2+ |

---

## 12. 兼容性说明

### 12.1 与 v1 的兼容性

| 兼容项 | 说明 |
|---|---|
| 模型向后兼容 | v1 老 JSON 文件可加载,缺失字段用默认值 |
| DI 注册向后兼容 | v1 的所有 DI 条目保留,v2 仅新增 |
| UI 壳向后兼容 | v1 的 TaskGraphWorkspaceControl(占位)被替换为真实现,名称不变 |
| ImportDialog 行为不变 | v1 的 [Import] 按钮继续走 `ITaskGraphGenerator` 规则路径(模式 ③) |
| 侧边栏按钮 / 路由不变 | v1 已接通的 sidebar 路由不修改 |

### 12.2 与 Chat 的兼容性

| 兼容项 | 说明 |
|---|---|
| `IAgentGateway` 接口兼容 | MVP 仅依赖现有方法;diff 能力若落地,需新增 gateway 接口或旁路 service |
| Chat 流式输出控件被复用 | 弹窗直接复用 ChatWorkspaceControl,不修改 Chat 自身逻辑 |
| Chat 历史与 Planner Session 隔离 | Planner Session 不写入 Chat 历史 |

### 12.3 文档交付

| 时机 | 文档 | 路径 |
|---|---|---|
| 已完成 | v1 方案 | `Docs/working/TaskGraph-方案.md` |
| 已完成 | v1 改造计划 | `Docs/working/TaskGraph-改造计划.md` |
| 已完成(v2 锁定后) | v2 编排方案 | `Docs/working/TaskGraph-编排方案.md`(本文件) |
| 待写(阶段 5) | v2 改造计划 | `Docs/working/TaskGraph-编排-改造计划.md` |
| 待更新(阶段 5) | AGENTS.md | 增加 "TaskGraph v2 编排" 章节 |
| 待更新(阶段 5) | `Docs/developer/architecture.md` | 第 78-79 行移除 "placeholder" 描述,补 v2 编排说明 |

---

## 13. 变更日志

| 日期 | 版本 | 变更 |
|---|---|---|
| 2026-06-19 | v2.0 | 锁定方案:三种输入模式 + 每节点独立 Session + JSON 解析 3 次重试 + 节点弹窗复用 Chat + 持久化升级 + 4 阶段实施路线 |
