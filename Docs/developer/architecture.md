# Architecture

## MVVM

- View models inherit from `ViewModelBase`
- Source-generated properties and commands use CommunityToolkit.Mvvm
- View models are partial when they use source generators

## Dependency Injection

- View models are registered as singleton
- Views are registered as transient
- Services are injected through interfaces
- `IAgentGateway` and `ISidebarRepository` are the two cross-cutting
  services that the chat and sidebar view models depend on

## UI

- `MainWindow` is the shell
- `MainWindow` hosts a 3-column grid: left sidebar (resizable, can be
  hidden) + center workspace + right sidebar (resizable, can be hidden)
- The center workspace header spans the chat area and the right sidebar
  top edge; it shows the active workspace title and a toggle for the
  right sidebar. 当前右侧边栏仅在 Chat 工作区可用；进入任务编排 /
  TaskGraph 工作区时，右侧边栏和对应开关都会隐藏
- `ActiveWorkspace` switches between `ChatWorkspaceViewModel`,
  `TaskGraphWorkspaceViewModel`, and `TaskOrchestrationWorkspaceViewModel`.
  Workspace VMs are resolved to their control via **explicit DataTemplates**
  in `App.axaml` — the `ViewLocator`'s "ViewModel" → "View" rename does not
  match the project's "Control" naming convention, so the templates are
  declared by hand to keep the rule out of the way.
- `SidebarControl` is the left navigation tree. Top quick actions keep
  新对话 / 搜索, while 项目 / 对话 are rendered as
  collapsible sections with hover-revealed action buttons;
  project session lists page in chunks of 5 with inline 展开显示 / 折叠显示 controls;
  session rows reserve a compact state slot that shows a spinner while the
  session is streaming and a blue dot after completion until the user opens
  that session
- The left sidebar toggle sits in the custom title bar, and the right
  sidebar toggle sits in the center workspace header
- Search opens as a top-anchored floating overlay with a dimmed backdrop,
  covers the full shell without affecting layout, and filters the current
  sessions by title; selecting a result opens that session and closes the
  overlay
- `ChatWorkspaceControl` is the main chat surface (fixed top header strip + message list + composer)
- `ChatWorkspaceViewModel` keeps per-session runtime state in memory, so
  switching the foreground chat no longer cancels another session's in-flight
  refresh or stream
- 长对话历史按“尾部优先”加载：打开会话时先只请求并渲染最近一段消息窗口，
  聊天区初始定位在底部；当用户把滚动条拉到顶部附近时，再触发向前扩窗加载更早历史，
  并在插入旧消息后补偿 `ScrollViewer.Offset` 以保持当前可视位置稳定
- 右侧 `Subagent` 卡片是双层滚动结构：外层面板负责整个右栏滚动，
  卡片内部 `ScrollViewer` 负责 400px 固定高度正文的 Markdown 浏览；
  子会话内容由消息序列聚合成完整 Markdown 摘要，不再只取最后一条预览；
  卡片不显示标题或运行状态文本；当 `Content` 更新时卡片内部自动滚动到底部
- Assistant messages show a left-side loading placeholder immediately
  after send; the placeholder is replaced by normal blocks when the first
  streamed block arrives
- 聊天消息项底部现在预留一条 hover 操作带：默认透明但始终占位，
  当鼠标移入消息本体或底部预留区域时显示操作按钮。当前包含
  `复制` 与 `分叉` 两个按钮，用户消息与助手消息都可复制，分叉按钮
  调用 OpenCode `POST /session/:id/fork` 基于目标消息创建新会话
- Thinking / Tool / Task blocks keep the spinner on the leading icon slot
  while running, then fall back to the static header once completed
- ChatWorkspaceControl only auto-scrolls when the viewport is already near
  the bottom, so manual upward scrolling is respected while fresh output
  still follows live updates
- The composer primary button switches between send and stop based on
  whether the draft box currently has content; it stays enabled while a
  response is streaming so the same control can cancel the send or queue
  the next draft
- While a response is streaming, additional composer submissions are
  added to a visible queue above the input and are sent automatically in
  order after the current response finishes
- Composer 输入框上方新增 Todo 条带：默认仅显示当前进行中的一条摘要，
  形如 `1/3 进行中 xxx`；鼠标悬停时自动展开当前会话全部 Todos。
  数据来自 OpenCode `GET /session/:id/todo` 与 SSE `todo.updated`，
  `ChatWorkspaceViewModel` 按会话缓存 Todo 列表并在切换会话时保留状态
- Composer 文本框支持 slash command 补全：当输入以 `/` 开头时，
  `ChatWorkspaceViewModel` 会从 OpenCode `GET /command` 拉取当前目录下
  可用命令，按前缀过滤后在输入框上方 popup 中展示；支持键盘上下选择、
  `Tab` 采纳、`Esc` 关闭
- Streamed chat blocks are created as soon as the first block header is
  detected, then their text/tool body is updated in place as later stream
  chunks arrive
- OpenCode part 到聊天块的当前映射规则包含三条特殊路径：
  `text` part 中的 `<think>` / `<thinking>` 标签拆成 `Thought` 块，
  `reasoning` part 直接映射成 `Thought` 块，
  `tool = "task"` 的 `ToolPart` 映射成 `Task` 块而不是普通 `Tool`
- `tool.state` 若出现未知枚举值，`OpenCode.Client` 需要保底反序列化，
  `OpenCodeAgentGateway` 再按普通 Tool 运行态渲染，避免 SSE 流因单个工具状态退出
- 结构化 question 交互走单独的数据流：`OpenCode.Client` 负责读取会话挂起问题列表并提交
  `answers[][]`，`ChatWorkspaceViewModel` 维护当前挂起问题状态与展开态，`MainWindow` 在标题栏下方渲染
  顶部确认面板，而不是把它塞进消息流或权限菜单；切换会话时仅保留消息内恢复入口，不自动展开面板
- `TaskGraphWorkspaceControl` is the TaskGraph orchestration workspace.
  It contains:
  - a saved-plan list backed by JSON files under the local app data directory
  - four creation modes: template / direct text / intent / document
  - a graph canvas that renders nodes by `TaskNode.Position` and edges by dependency
  - node cards can be repositioned by drag-and-drop; dependencies can be created either from the side panel or by dragging from a node's link handle onto another node
  - node cards support inline editing: selected nodes can switch into an in-card edit mode for title / kind / description without using the shell sidebar
  - an editor toolbar for add/delete node, auto-layout, and zoom controls
  - save / use-in-chat / more are merged into the graph editor toolbar; standalone execute controls were removed
  - a graph focus mode that collapses both TaskGraph side panels and asks the shell to hide the outer left/right sidebars so the canvas fills the window
  - an empty-canvas onboarding state so the graph surface is visible even before the first graph exists
  - three built-in templates: task list / feature development / bug list
  - a waiting-for-input checkpoint for feature-development graphs before plan execution continues
  - node detail entry points that open a dedicated session-detail window
- `SettingsWindow` is a separate dialog
- Global styles stay in `App.axaml`

## Sidebar Actions

The sidebar surface the following actions; each is wired through a
`RelayCommand` on `SidebarViewModel` and handled by
`MainWindowViewModel` so dialogs and I/O stay in the shell layer:

| Surface                  | Action                  | Effect                                              |
|--------------------------|-------------------------|-----------------------------------------------------|
| Top button "新对话"      | Start a new chat        | Open blank page in chat VM, using current project  |
| 任务编排 section header "+" | New task graph      | Switch to TaskGraph workspace and clear current graph |
| 任务编排 row click       | Open task graph         | Switch to TaskGraph and load the selected graph    |
| 任务编排 row "..."       | More menu               | 重命名任务编排 / 移除                              |
| Project section header "+" | Add project          | Open folder picker → `Sidebar.AddProjectAsync`      |
| Project section header "..." | (reserved)         | No-op in v1                                         |
| Project row "+"          | New session in project  | Set current project, open blank page                |
| Project row "..."        | More menu               | 置顶项目 / 在资源管理器中打开 / 重命名项目 / 移除    |
| Project session list     | Page sessions           | Show 5 at a time, then expand in batches of 5       |
| Session row "..."        | More menu               | 重命名对话 / 移除                                   |
| 任务编排 / 对话 section header click | Expand / collapse | Toggle that section                         |
| 项目 section header click | Expand-all / collapse-all | Toggle all project expand states          |
| Project row click        | Expand + focus          | Toggle expand and set current project               |
| Session row click        | Open session            | Switch to chat and load history                     |

The two menus (`...`) are inline popups built in code-behind from a
shared `RowActionPopup` element so we don't need a separate XAML file
per menu. `SidebarProjectViewModel` is the per-project tree node that
owns the project record, the `Sessions` / `VisibleSessions`
collections, the expand / current / pinned flags, and the
`VisibleSessionCount` paging counter (5 at a time, with inline
`展开显示` / `折叠显示` controls).

## Cross-cutting State

- `IAppSettingsService` persists `LastProjectId` to
  `%LOCALAPPDATA%/AgentOrchestrator/appsettings.local.json` on every
  current-project change so the next launch opens a blank page
  against the same working directory.
- `ProjectsTracker.CurrentWorkingDirectory` (static slot) is the
  short-term "what should the next session use" working directory.
  Set by `ChatWorkspaceViewModel.OpenBlankPage(workingDirectory)`,
  consumed by the same VM's `SendAsync` when creating a new session.
- The `sessions` table also tracks `last_activity_at` (refreshed on
  every stream / message change) and `viewed_at` (set when the user
  opens the session). The sidebar uses the pair to render the
  compact streaming spinner / "new since last view" blue dot, and
  the CLI seeds both fields when creating new sessions via the
  `new-session` smoke test.

## Layered Services

```
View ── ViewModel ── IAgentGateway / ISidebarRepository
                              │                │
                  OpenCodeAgentGateway  SqliteSidebarRepository
                              │                │
                       OpenCodeClient    Microsoft.Data.Sqlite
                              │
                       OpenCode HTTP server
```

- `IAgentGateway` is agent-agnostic (`AgentKind = "opencode"` in v1);
  the chat VM never references `OpenCode.Client.*` types
- `IAgentGateway.GetMessagesAsync(agentSessionId, limit)` 支持按消息数限制历史窗口；
  当前 OpenCode 后端只有 `limit` 没有 cursor / before，因此“向上翻页”实现为
  扩大 limit 重新取最近 N 条，再只把本地尚未显示的更早消息 prepend 到顶部
- `ISidebarRepository` is the local SQLite store for project and
  session metadata (no message content is persisted locally)
- `ITaskGraphStore` is a JSON-backed local store for TaskGraph plans;
  it persists the graph structure, execution state, node session ids,
  summaries, and recovery metadata under `%LOCALAPPDATA%/AgentOrchestrator/Datas/TaskGraphs`
- `ITaskTemplateStore` no longer exists — templates and runtime graphs
  share `ITaskGraphStore` and are distinguished by `TaskGraph.DocumentKind`
  (Runtime vs Template). All `TaskGraph` JSON files live under
  `%LOCALAPPDATA%/AgentOrchestrator/Datas/TaskGraphs/` with prefixed file
  names: `template.{id}.json` for templates and `runtime.{id}.json` for
  runtime graphs (legacy unprefixed `{id}.json` files remain readable
  for backward compat)
- `ITaskGraphTemplateInstantiator` (`TaskGraphTemplateInstantiator` impl)
  is the transformation service that turns a `TaskGraph` template into a
  fresh runtime graph: deep-clone via JSON roundtrip, give every node a
  fresh id and rewrite `DependsOn` references, sync the metadata
  references (`FixedNodeIds`, `AnchorNodeId`, `InsertAfterNodeId`,
  `ConnectToTerminalNodeId`) to the new ids, clear every runtime state
  field on graph and nodes, then perform controlled dynamic-zone
  expansion per the template's `TaskGraphTemplateMetadata.DynamicZones`
  (parse the user input as lines, generate up to `MaxGeneratedNodeCount`
  `Execute` nodes, chain them in series, wire the first to the splice
  anchor and the last to the terminal node, respect `FixedNodeIds`)
- `BuiltInTemplateSeeder` seeds the 4 built-in templates on first init
  (3 user-facing: `builtin.task-list` / `builtin.feature-dev` /
  `builtin.bug-list`, plus 1 hidden: `builtin.auto-orchestration` used
  by Chat's "自动编排" mode). Seeding is idempotent — built-in ids are
  stable and the seeder skips any id that already exists
- `TaskTemplateMigrationService` is a one-time, fire-and-forget legacy
  data migrator. It scans the legacy `%LOCALAPPDATA%/AgentOrchestrator/Templates/`
  directory for old pre-refactor template JSON files, reads them directly
  via `JsonDocument` (the old `TaskTemplate` model class no longer
  exists), converts each to a `TaskGraph` template document, saves it to
  the new store, and moves the source file to `Templates/_migrated/`.
  Safe to run multiple times; no-op once all data is migrated
- `OpenCodeAgentGateway` wraps `OpenCodeClient`, translates Part/Message/
  ToolState into the chat-block vocabulary, routes SSE events into
  streaming `ChatStreamChunk` envelopes, forwards `todo.updated` into
  agent-agnostic Todo snapshots, queries child sessions plus session
  status for subagent activity snapshots, and additionally exposes
  command list / command execution / session fork / session revert
  through `IAgentGateway`
- slash command 当前分两类执行路径：
  - 普通命令：调用 `POST /session/:id/command`，执行后整段重新拉取当前会话消息
  - `/revert`：在当前实现中回退最近一条助手消息，调用
    `POST /session/:id/revert` 后刷新当前消息列表
- `TaskGraphExecutor` runs graph nodes against `IAgentGateway` with one
  agent session per node, persists state after each node transition,
  reconciles interrupted `Running` nodes back to `Failed` on reload,
  pauses feature-development graphs at the human-confirmation node, and
  performs scoped runtime graph expansion after the generated-plan node
- `TaskGraphRuntimeHub` is an in-memory event bridge between the executor
  and node-detail windows; it forwards streamed chunks and node-state
  changes without coupling the executor to UI types
- `TaskGraphChatMapper` reuses the existing chat block vocabulary
  (`ChatMessageViewModel` / `ChatBlockViewModel`) so node-detail windows
  can render remote history and live stream output with the same markdown /
  thought / tool / task surfaces as the main chat workspace
- `TaskGraphTemplateBuilder` materializes the built-in graph templates:
  linear task chains, feature-development graphs with confirmation and
  dynamic-plan expansion, and bug-list graphs with decision branches plus
  a final report node
- `TaskGraphDynamicExpander` holds the template-specific runtime rules for
  scoped graph mutation and decision-based node skipping
- `TaskGraphBugStructuredParser` parses bug decision/review JSON output,
  drives branch selection from structured fields, and assembles the final
  bug report locally instead of relying on free-form model prose
- bug-list graphs expose the assembled report twice in the UI:
  as a dedicated right-side report panel for inspection and as the report
  node's output summary for execution trace continuity; the panel can export
  Markdown and JSON snapshots to the local exports directory
- 流式正文 / Thinking 的真实来源不是只看 `message.part.updated`
  快照；`OpenCode.Client` 需要把 `message.part.delta` 也反序列化出来，
  `OpenCodeAgentGateway` 再按 `partID` 将 delta 追加到现有块，
  否则聊天区会退化成“整块完成后才一起出现”
- `ChatWorkspaceViewModel` does a final `GetMessagesAsync` reconciliation
  after a send stream completes so tool blocks reflect the server's
  terminal state even when the last SSE tool update was only `running`
  and refreshes subagent activity while the current session is streaming
- The CLI (`AgentOrchestrator.Cli`) reuses the same services for
  headless verification

## TaskGraph In-Conversation Orchestrator

The chat workspace acts as the control plane for TaskGraph execution;
the executor is the execution plane. Both surfaces communicate through
`TaskGraphRuntimeHub` events and `ConversationExecutionContext`.

- **Control plane** — `ChatWorkspaceViewModel` owns `ActiveGraph`,
  `ActiveExecutionContext`, and `HasChatExecutionLease`. It triggers
  graph execution via `ITaskGraphExecutionController.StartAsync(...)`
  and consumes checkpoint events back through hub subscriptions. The
  chat composer is gated by the lease so `InSessionExecution` and
  `Inline` node runs cannot collide with regular chat sends.
- **Workspace handoff** — `TaskGraphWorkspaceViewModel.UseCurrentInChat`
  persists the current graph and raises `UseInChatRequested`; the shell
  switches to `ChatWorkspaceViewModel`, which calls
  `StartExistingTaskGraphAsync(...)` so the same persisted graph entity
  executes under Chat's checkpoint/status surface instead of a separate
  TaskGraph-page run button set. This is the "in-graph" path: the
  template selection in the chat's "使用模板" mode is a separate
  "out-of-graph" path that goes through the orchestration workspace
  (see "Task Orchestration Independent Workspace" above)
- **Execution plane** — `TaskGraphExecutor` implements both
  `ITaskGraphExecutor` (existing entry points, unchanged signatures)
  and `ITaskGraphExecutionController` (new `StartAsync` / `ResumeAsync`
  / `PauseAsync` / `CancelAsync`). The internal loop runs node-by-node
  in topological order and pauses at checkpoints; the same loop is
  shared by all four `ITaskGraphExecutor` entry methods so the
  standalone TaskGraph workspace continues to use the same engine.
- **Checkpoint kinds** — first-version checkpoints fire on
  `NodeCompleted`, `NodeFailed`, `WaitingForInput`, and `GraphExpanded`
  (plan §8.2). A checkpoint sets `TaskGraph.IsCheckpointPending`,
  populates `ActiveCheckpointNodeId`, publishes
  `TaskGraphCheckpointEventArgs` through the hub, and awaits a
  `GraphContinueDecision` (`Continue` / `Pause` / `Cancel` /
  `RetryFailed` / `SkipNode` / `Summarize`).
- **Node delegation strategies** — every `TaskNode` carries a
  `TaskNodeDelegationStrategy` of `NewSession`, `ChildSession`,
  `InSessionExecution`, or `Inline`. `NewSession` is the existing
  behavior; the other three were added in v3 with `Inline` returning a
  deterministic stub for the first version (LLM-routed inline is
  deferred).
- **Context bridge** — `ConversationExecutionContext` is the chat
  session's projection of graph state. It carries three most-recent
  completed nodes, two most-recent failed nodes, pending decisions,
  and recent mutations. `BuildTaskGraphContextInjection()` renders
  this projection as a hidden prompt prefix; `SendOneAsync` prepends
  it to the next user prompt and clears the context after one use so
  the chat history is not polluted.
- **Lease semantics** — `ChatExecutionLease` is acquired when a
  chat-triggered graph enters `InSessionExecution` or `Inline`
  execution and is released when the graph reaches a terminal state or
  is cancelled. While held, `HasChatExecutionLease` is `true` and the
  executor's checkpoint/decision flow has exclusive write authority
  on the bound chat session.
- **IAgentGateway extensions** — `CreateChildSessionAsync(parent, ...)`
  and `ListChildSessionsAsync(parent, ...)` were added to support
  `ChildSession` delegation. `OpenCodeAgentGateway` resolves the
  parent's directory and reuses `Sessions.CreateAsync` and
  `Sessions.ChildrenAsync`; `NotImplementedAgentGateway` mirrors the
  stub pattern.
- **UI surface** — `ChatWorkspaceControl` exposes an orchestration
  strip between the message scroll area and the composer with two
  mutually-visible states: a mode selector when no graph is active,
  and a status card (graph name, running node, completion/failure/
  decision counts, pause/cancel/continue/summarize/detach buttons)
  when `HasActiveGraph` is true. 编排模式当前为 `普通对话 / 自动编排 / 使用模板`；
  选择模式本身不会立即执行，真正执行发生在点击主发送按钮时；选择
  `自动编排` 时，Chat 加载内置的 `builtin.auto-orchestration` 模板并通过
  `ITaskGraphTemplateInstantiator` 主链路实例化运行图（带 `BasedOnTemplateId`
  回写）后启动 chat 内执行；选择 `使用模板` 时，Chat 触发
  `TemplateOrchestrationRequested`（payload 含 prompt + agent session id +
  working directory），shell 切到独立任务编排工作区并交给
  `TaskGraphDocumentEditorViewModel` 处理 — 用户选模板 + 填输入 →
  `InstantiateCommand` → `GraphInstantiated` 事件 → shell 切到
  任务图页打开新图。All bindings use compiled bindings with `x:DataType`.

## Task Orchestration Independent Workspace

`TaskOrchestrationWorkspaceViewModel` is a top-level workspace reachable
from a dedicated title-bar button. It is independent from the chat
sidebar, and the main left navigation no longer renders a top-level
`任务编排` section.

- **Unified domain** — templates and runtime graphs are both
  `TaskGraph` documents distinguished by `TaskGraph.DocumentKind`
  (`Runtime` vs `Template`). A template IS a graph: it has the same
  node / edge / metadata shape, is persisted in the same directory
  (`Datas/TaskGraphs/`) with the `template.{id}.json` prefix, and is
  loaded by the same `ITaskGraphStore.LoadTemplateAsync` method.
  Templates are not directly executable; instantiation is the act of
  cloning a template, clearing all runtime state, performing controlled
  dynamic-zone expansion, and saving the result as a `Runtime` graph
  with `BasedOnTemplateId` set
- **Built-in templates** — `BuiltInTemplateSeeder` seeds four on first
  init: 3 user-facing (`builtin.task-list`, `builtin.feature-dev`,
  `builtin.bug-list`) plus 1 hidden (`builtin.auto-orchestration`,
  consumed by Chat's "自动编排" mode and hidden from the orchestration
  sidebar's `模板` group). They carry `IsBuiltInTemplate = true`, are
  read-only, and the seeder skips any id that already exists so user
  edits to the name survive
- **Template metadata** — every template carries a
  `TaskGraphTemplateMetadata` payload:
  `AllowDynamicExpansion` (gates the instantiator's expansion pass),
  `FixedNodeIds` (unmutable anchor nodes), `FixedEdgeKeys`
  (unmutable edges), `DynamicZones` (list of `DynamicZoneDefinition`
  with `AnchorNodeId`, `InsertAfterNodeId`, `ConnectToTerminalNodeId`,
  `AllowParallelNodes`, `MaxGeneratedNodeCount`, `GenerationInstruction`),
  plus three rule string lists (`NodeGenerationRules`,
  `EdgeGenerationRules`, `ExecutionRules`)
- **Node-level template flags** — `TaskNode` carries three template-only
  fields: `IsTemplateLocked` (fixed skeleton node, not removed at
  instantiation), `IsDynamicPlaceholder` (an expansion anchor), and
  `TemplateRole` (semantic role: `Input` / `Plan` / `Checkpoint` /
  `ExpansionAnchor` / `ExpansionTerminal` / `Report`)
- **Left nav** — `TaskOrchestrationWorkspaceControl` renders a 320px
  panel with: top quick actions (`新任务图` / `搜索`), a `模板` group
  with template rows (name + `TemplateKindText` for the kind name + an
  `内置` tag for built-ins + hover `...` for 重命名 / 复制 / 删除), a
  `+` action on the group header to create a new custom template
  (which becomes a `TaskGraph` template document), and a `任务图` group
  with runtime graph rows (name + second-line project-or-summary text +
  updated + hover `...` for 删除 / 重命名 / 修改所属项目). Both groups
  use the same `SidebarTaskGraphItemViewModel` row VM, distinguished
  only by `DocumentKind`. `修改所属项目` 当前通过轻量选择弹窗实现，
  列出全部已有项目，并提供 `无所属项目` 选项
- **Right pane** — hosts the shared `TaskGraphWorkspaceControl` on top
  (the same canvas control used by the independent TaskGraph workspace)
  plus a `TaskGraphDocumentEditorViewModel`-backed editor panel below.
  When a template is selected, the editor shows template-only fields
  (`TemplateNotes` draft, `TemplatePlannerPrompt` draft,
  `AllowDynamicExpansion` checkbox, `DynamicZones` ItemsControl with
  per-zone editor, `保存模板` button, and a `基于此模板生成任务图`
  button bound to the editor's `InstantiateCommand`). When a runtime
  graph is selected, the editor shows runtime info only (name, a
  read-only template-mode indicator, and a note that execution controls
  live in the canvas toolbar). Execution buttons in the canvas
  (`CanExecute` / `CanRetryFailed` / `CanCancelExecution` /
  `CanContinue`) all return false when `IsTemplateDocument` is true;
  the `在 Chat 中调用` button is hidden in template mode
- **Canvas reuse** — both the orchestration workspace AND the
  independent TaskGraph workspace share the same
  `TaskGraphWorkspaceViewModel` singleton. The orchestration VM holds
  a reference to that singleton; when a row is selected, it loads the
  document into the shared canvas via `OpenTemplateByIdAsync` (no
  `ReconcileAsync` since templates have no execution state) or
  `OpenGraphByIdAsync` (full reconcile for runtime graphs). The
  canvas's `IsTemplateDocument` / `IsRuntimeDocument` properties
  drive the toolbar's enable state and the "use in chat" visibility
- **Instantiation handoff** — when the editor's `InstantiateCommand`
  creates a runtime graph, it raises `GraphInstantiated` with the new
  graph as the payload. The shell subscribes to this event on the
  editor and switches to the TaskGraph workspace via
  `OpenGraphByIdAsync(graph.Id)`, so the user lands on the canvas view
  of the just-instantiated graph. Chat's "使用模板" flow goes through
  the same path: chat's "使用模板" → switch to orchestration workspace
  → user picks template + enters input → editor's `InstantiateCommand`
  → shell's `GraphInstantiated` handler → open new graph in canvas
- **Shell integration** — `MainWindowViewModel` subscribes to four
  events on the orchestration VM:
  - `NewTaskGraphRequested` → switch to `TaskGraph` and run
    `NewGraphAsync`
  - `TaskGraphOpenRequested(id)` → switch to `TaskGraph` and run
    `OpenGraphByIdAsync(id)`
  - `TaskGraphActionRequested` → reuse the existing sidebar action
    pipeline (Rename / Remove) for the embedded task-graph rows
  - `Editor.GraphInstantiated(graph)` → switch to `TaskGraph` and open
    the new graph in the canvas
- **Chat integration** — Chat's "自动编排" and "使用模板" both go
  through the same `ITaskGraphTemplateInstantiator` main path as the
  orchestration workspace. "自动编排" loads the hidden
  `builtin.auto-orchestration` template, instantiates it with the
  chat input as `UserInput`, sets the result as `ActiveGraph`, and
  starts execution. "使用模板" raises `TemplateOrchestrationRequested`
  (payload: prompt + chat session id + working directory); the shell
  switches to the orchestration workspace and lets the user pick a
  template + input
- **CLI** — `AgentOrchestrator.Cli orchestration open` prints the
  hint; the App accepts `--open-orchestration` on launch to switch to
  the new workspace (used by `scripts/verify-orchestration-workspace.ps1`
  to capture the layout for regression checks)
- **Styles** — all new classes live in `App.axaml` under the `orch-*`
  prefix (`orch-quick-action`, `orch-search-input`, `orch-inline-edit`,
  `orch-detail-card`, `orch-detail-title`, `orch-detail-meta`,
  `orch-status-pill` with state variants, `orch-primary-btn`,
  `orch-secondary-btn`, `orch-detail-input` for both TextBox and
  ComboBox, `orch-empty-hint`)

## Settings

- `AppSettings` is a single POCO that carries every persisted field
  (`OpenCodeEnabled`, `Host`, `Port`, `Username`, `Password`,
  `UiFontFamily`, `CodeFontFamily`, `UiFontSize`, `CodeFontSize`,
  `LastProjectId`); the layout is intentionally flat so the JSON
  override file stays diff-friendly
- `JsonAppSettingsService.Load()` is a three-layer merge:
  1. compiled defaults from `new AppSettings()`
  2. the embedded `appsettings.json` resource shipped with the binary
  3. the local file at `%LOCALAPPDATA%/AgentOrchestrator/appsettings.local.json`
  Later layers override earlier ones; corrupt local files are ignored
  silently so a bad edit cannot brick startup
- `SettingsWindow` exposes a three-page rail:
  `个人 / 常规` (placeholder), `个人 / 外观`, `集成 / 服务`
- `个人 / 外观` exposes UI / code font family and size in px; the
  font family fields are displayed read-only, while the size fields
  accept free-form input
- `集成 / 服务` is a card list; `OpenCode` shows a derived URL
  (`http://{Host}:{Port}`), enable toggle, live connection status,
  username, password (with show/hide toggle), and a `codex`
  placeholder entry
- Appearance values are applied once on app startup
  (`App.axaml.OnFrameworkInitializationCompleted`) by writing to
  `Application.Current.Resources` so every view that references the
  `UiFontFamily` / `UiFontSize` / `CodeFontFamily` / `CodeFontSize`
  DynamicResource tokens picks up the saved values automatically
- UI 默认字体链为 `SimHei, Segoe UI, Microsoft YaHei UI, Microsoft YaHei`；
  `JsonAppSettingsService.Load()` 会把旧配置中的 `Source Han Sans`
  和旧默认 `Segoe UI, Microsoft YaHei UI, Microsoft YaHei`
  自动迁移到这条系统字体链，确保 Windows 桌面端普通中文文本优先按黑体渲染
- `OpenCodeEnabled` is the runtime gate: `SettingsViewModel`
  short-circuits the health check and reports the service as `断开`
  whenever the flag is off; live connection status is refreshed
  whenever the flag, host, port, username, or password changes
- `SettingsViewModel.Ok()` and `MainWindowViewModel` each write their
  own slice of `AppSettings` to the same local file. The settings VM
  persists appearance + OpenCode service fields on confirm; the shell
  persists `LastProjectId` + the live OpenCode service fields on
  every sidebar focus change. Whichever runs last wins on the shared
  fields
- `SettingsViewModel.OpenServicesPage()` lets the shell jump straight
  to the services page when the title-bar service button is clicked

## Current Services

- `MarkdownRenderer` handles Markdown rendering for chat content
- `ISidebarRepository` / `SqliteSidebarRepository` (P1)
- `IAgentGateway` / `OpenCodeAgentGateway` / `NotImplementedAgentGateway` (P2-P3)
- `DataPathProvider` resolves `.exe/Datas/OrchestratorDb.db`
- `IAppSettingsService` (extended with `LastProjectId`)
- `DialogHost` provides code-only Confirm/Input/Select Windows used by the
  sidebar rename + remove flows and lightweight ownership selection
- `ToolDisplayParser` converts a tool call's name + JSON input +
  working directory + output into a structured `ToolDisplayInfo`
  (`PrimaryText`, relative `FilePath`, optional `LineRangeText`,
  optional `CodeText`); the parser searches well-known argument keys
  (`path`, `file`, `filePath`, `filepath`, `filename`, `target*`,
  `oldPath`/`newPath`) and walks nested objects/arrays when needed

## Chat Block Tool Display

- `RemoteBlock` carries the tool call's JSON `Input` alongside the
  existing `ToolName` / `ToolOutput`; `OpenCodeAgentGateway.ResolveTool`
  reads `tool.State.Input` from any of `ToolStatePending` /
  `ToolStateRunning` / `ToolStateCompleted` / `ToolStateError` and
  packages it as the 4th tuple element so the view-model never has to
  re-parse the OpenCode event payload
- `IChatBlock.ToolInput` is the contract that surfaces the same JSON
  dictionary to `ChatBlockViewModel`, which forwards it to
  `ToolDisplayParser.Parse(...)`; `ToolWorkingDirectory` (set on every
  tool block by `ChatWorkspaceViewModel.CreateToolBlock`) is the
  anchor the parser uses to convert absolute paths to project-relative
  paths via `Path.GetRelativePath`
- Collapsed Tool / Task headers in `CollapsibleBlockControl` render
  four structured spans: action verb, optional relative file path,
  optional line range, with separate `IsVisible` flags driven by
  `ShowToolHeader` / `HasToolHeaderFilePath` /
  `HasToolHeaderLineRange`
- When the tool output contains numbered lines (`123: ...`) or a
  fenced code block, the parser extracts the body into
  `ToolDisplayInfo.CodeText`; `CollapsibleBlockControl` renders that
  body via the dedicated `ReadOnlyCodeBlock` control instead of
  falling through to `MarkdownRenderer`
- `ReadOnlyCodeBlock` is a `UserControl` that wraps a read-only
  `TextBox` (`Classes="tool-code-viewer"`) inside a `ScrollViewer` with
  `MaxHeight=300`, so long tool bodies scroll within the same 300px
  envelope that thinking blocks use
