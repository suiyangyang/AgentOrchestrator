# Current Project State

## Application

- Desktop app only
- `net10.0`
- Avalonia 12.0.4
- CommunityToolkit.Mvvm 8.4.1
- DI via Microsoft.Extensions.DependencyInjection 10.0.9
- Local persistence: SQLite via `Microsoft.Data.Sqlite`
- Local DB lives at `{AppContext.BaseDirectory}/Datas/OrchestratorDb.db`
- User settings (including `LastProjectId`) live at
  `%LOCALAPPDATA%/AgentOrchestrator/appsettings.local.json`; the layered
  default flow is `new AppSettings()` → embedded `appsettings.json` (compile-time)
  → local override file (later wins)

## Code Layout

- `Program.cs` boots Avalonia
- `App.axaml.cs` creates the service provider
- `App.axaml` declares explicit DataTemplates so `MainWindowViewModel`
  → `ChatWorkspaceViewModel` → `ChatWorkspaceControl` resolves
  correctly without depending on the `ViewLocator` convention
- `MainWindowViewModel` owns `Chat`, `TaskGraph`, `TaskOrchestration`, `Sidebar`, `Settings`
  and an `ActiveWorkspace` that switches between the three workspace VMs.
  It is also the only place that knows about dialogs (folder picker,
  rename, remove-confirm) — the sidebar and the new orchestration workspace
  raise events and the shell handles them.
- `MainWindowViewModel` also owns left/right sidebar visible state and
  widths, so the shell can toggle and resize both sidebars without
  leaking layout state into child workspaces
- `ChatWorkspaceViewModel` manages messages, attachments, permissions,
  draft input, header title, subagent activity, and the full session lifecycle (new / open / send /
  stream) via `IAgentGateway` and `ISidebarRepository`
- `TaskGraphWorkspaceViewModel` now owns the full task orchestration资产管理 flow:
  saved graphs, template-based creation, direct/intent/document plan
  generation, graph-canvas projection, node drag/move, dependency link
  editing (side-panel selection and canvas drag-link), zoom/auto-layout,
  node add/delete/edit, bug report aggregation/export, node-detail routing,
  and the handoff entry that invokes the current TaskGraph from Chat
- TaskGraph 图形区支持“铺满窗口”模式：进入后会收起 TaskGraph 内部左右栏与主窗口外层左右侧栏，只保留图编辑区和画布工具条
- `Models/TaskGraph/` contains the persisted graph model
  (`TaskGraph`, `TaskNode`, template/execution enums, edges, planner schema)
- `Services/TaskGraph/` contains the orchestration runtime:
  direct parser, document reader, LLM planner, JSON store, topology
  helper, template builder, dynamic expander, output injector,
  execution runtime, runtime event hub, and chat-message mapper for
  node detail rendering
- TaskGraph 当前支持三种模板新建：
  - 任务列表：将每一行任务转换成串行节点链
  - 复杂功能开发：需求输入 → 方案生成 → 用户确认 → 计划生成 → 运行期注入开发节点 → 执行收尾
  - Bug 列表：逐个 bug 做读取、分析、信息充分性判断、修复/未解决分流，最后汇总报告
- 功能开发模板的动态编排范围当前是受控的：
  只有 `feature_plan` 节点完成后，才会根据其 JSON 输出向当前图中注入执行节点；
  这不是通用工作流引擎级别的任意运行期扩图
- Bug 列表模板中的 `Decision` 节点当前通过输出约定
  JSON `decision = EnoughInfo | NeedMoreInfo` 来驱动后续节点跳过或继续；
  `review` 节点输出结构化 `resolved / reliability / reason / verification`，
  最终报告节点则在本地汇总成 Markdown 表格
- 每个 `SessionRuntimeState` 还缓存历史窗口大小、是否还有更早历史、
  是否正在加载更早历史；切换会话时会保留各自的历史加载进度，
  不再每次都重新全量建树
- Thinking / Tool / Task 折叠块在运行态只显示前置转圈状态图标，
  不再在标题后追加文字状态标识；完成后恢复静态标题样式
- 聊天区消息列表在滚动条接近底部时会自动跟随最新消息；
  用户主动上拉后不会强制拉回底部，只有接近底部时才继续贴底
- 打开历史较长的会话时，聊天区首屏只加载最近一段历史并默认贴底；
  用户上拉到顶部附近时再继续向前补页，同时保持当前阅读位置稳定不跳动
- 输入区主按钮会根据当前草稿状态在“发送”和“停止”之间切换；
  发送过程中按钮仍保持可用，这样既能取消当前回复，也能继续把新草稿加入队列
- 当发送管线已经启动但当前回复尚未结束时，新的发送请求会先进入输入区上方的可见队列，
  当前回复完成后按顺序自动续发；队列项支持回填到输入框继续编辑或直接移除
- Assistant 流式消息在收尾同步远端历史时，必须以按 `partId`
  增量合并为准，不能用一份可能尚未完全落稳的远端块列表直接整包替换
  当前 `Blocks`，否则会造成前台短暂丢失 `thinking` / `tool`
  折叠块，而重新打开会话后又恢复
- `OpenCodeAgentGateway.SendMessageAsync` 必须在触发
  `prompt_async` 后立即持续消费 SSE 事件，不能等待 HTTP 调用完整结束后再开始读流；
  `OpenCode.Client` 还必须识别 `message.part.delta`，因为
  `message.part.updated` 只保证快照更新，真正驱动正文 / thinking /
  tool 输入流式刷新的增量事件是 `message.part.delta`
  文本 part 的 `<think>` / `</think>` 以及 `<thinking>` / `</thinking>` 需要按累计文本渐进拆块，这样 UI 才能在
  thinking 打开时先创建折叠块，并继续向其中追加内容
- OpenCode 的 thinking 还可能直接以 `reasoning` part 流出；
  这类 delta 不能再走普通文本拆块，否则会被误归到正文 `Text`，必须稳定映射到 `Thought`
- OpenCode 父会话里的任务活动当前真实表现为 `tool = "task"` 的 `ToolPart`，
  不是独立的 `subtask` 可视块；聊天区需要把它映射成专门的 `Task` 折叠块，
  否则实时流和历史加载都会把任务内容埋进普通 Tool 文本
- `ToolPart` 的 `tool.state` 可能出现当前客户端不认识的状态；
  这种情况必须降级显示为普通 Tool 运行态，不能让聊天流异常退出
- OpenCode 的结构化提问不是普通 permission 弹窗，而是会返回带
  `questions / options / custom` 的挂起 question 列表；当前 UI 使用悬浮在主窗口内容区上方的确认面板，
  不再占用标题栏下方的固定布局高度，用户勾选或输入答案后再统一提交
- `SidebarViewModel` owns the project + session tree, the search
  overlay state, search result list, current-project focus, the
  per-row "..." actions, and the project-session paging state that
  shows at most 5 chats per project by default. 任务编排入口已从主侧栏抽离，
  不再作为左侧顶层导航分组出现
- `TaskOrchestrationWorkspaceViewModel` is the top-level "任务编排" entry's VM.
  It owns its own list of templates (from `ITaskTemplateStore`) and task graphs
  (from `ITaskGraphStore`), search, group-expand state, current selection
  (template or task graph), and inline template rename. It exposes three
  events the shell routes to the right destination:
  `NewTaskGraphRequested` / `TaskGraphOpenRequested` (string id) /
  `TaskGraphActionRequested` / `TemplateGenerateRequested`. The right pane
  shows template detail (name / description / base kind / default input /
  save / 本次生成输入 / "基于此模板生成任务图") or task-graph summary (name / status pill /
  node count / updated / "打开图编辑"). Built-in templates (task-list /
  feature-dev / bug-list) cannot be deleted; they can be duplicated to
  create a custom copy. `TaskGraph` 现已持久化 `ProjectId / ProjectName`
  作为导航归属信息，任务图列表第二行优先显示所属项目；无项目时回退显示模板、
  节点数和状态摘要。修改所属项目时，shell 会弹出已有项目选择框，并提供
  `无所属项目` 选项
- `MainWindow.axaml` puts a `任务编排` title-bar button between the existing
  sidebar toggle and the right-side controls. Clicking it calls
  `MainWindowViewModel.OpenOrchestrationWorkspace()` which sets
  `ActiveWorkspace = TaskOrchestration`. The DataTemplate in `App.axaml`
  maps `TaskOrchestrationWorkspaceViewModel` to the new
  `TaskOrchestrationWorkspaceControl`.
- `MainWindow.axaml` hosts a 3-column shell with resizable left and
  right sidebars; the center column contains a fixed header area that
  spans the workspace and right sidebar top edge, plus the active
  workspace body
- 标题栏左侧包含一个与主界面图标风格一致的单色服务按钮；
  点击后直接弹出 `SettingsWindow` 并定位到 `设置 / 集成 / 服务`
- 右侧 `Subagent` 区域使用固定高度卡片展示子会话活动；卡片正文按
  Markdown 渲染并支持内部滚动，默认高度为 `400`，展示该子会话的完整信息汇总，
  而不是仅显示最后一条消息；底部显示 `Agent 名称 · Model 名称 · 耗时`；
  卡片整体字体族使用 `Consolas, Microsoft YaHei UI, Microsoft YaHei, SimHei`，
  卡片标题 / 状态 / 正文 / 底部元信息基础字号为 `12`，Markdown 标题保留分级字号；
  当 subagent 内容字段刷新时，卡片内部滚动条会自动贴到底部
- `ChatWorkspaceControl` adds a fixed header strip inside the chat
  workspace for task orchestration and subagent activity;
  当没有活动编排时，编排入口位于输入区底部工具条中，处在权限右侧、
  模型左侧，并使用与权限设置一致的弹出式选项框。当前选项为
  `普通对话 / 自动编排 / 使用模板`：普通对话直接发送，自动编排直接启动
  chat 内任务图，使用模板则切换到独立任务编排工作区并带入当前输入；
  独立任务图页不再单独保留顶部资产工具条，“保存 / 在 Chat 中调用 / 更多”
  已并入图编辑工具栏，不再提供单独的执行按钮。
  当已有活动编排时，顶部 strip 显示当前编排状态与暂停 / 取消 / 继续 / 汇总 / 分离控制
- `TaskGraphNodeDetailWindow` is a separate transient window that shows
  one node's remote message history plus live streamed output, reusing
  the same chat block controls as the main chat surface
- `SettingsWindow.axaml` is a fixed-size settings dialog with a left
  two-level navigation rail. The top-level groups are `个人` and `集成`;
  the second level currently exposes `常规` (placeholder), `外观` and
  `服务`. `外观` exposes UI / code font family and font size (in px);
  `服务` renders one card per integration. `OpenCode` exposes enable
  state, live connection status, derived URL, username, and password
  (with show/hide toggle); `codex` is a disabled placeholder entry.
- 标题栏左侧的服务按钮触发 `MainWindowViewModel` 打开 `SettingsWindow`
  并直接定位到 `集成 / 服务` 页，与左侧服务卡片形成快速回环
- Appearance values are read once on app startup and pushed into
  `Application.Current.Resources["UiFontFamily" | "UiFontSize" |
  "CodeFontFamily" | "CodeFontSize"]` as DynamicResource tokens, so
  every view that references those tokens picks up the saved values
  automatically
- UI 默认字体链是 `Segoe UI, Microsoft YaHei UI, Microsoft YaHei`，
  以尽量贴近 Windows 浏览器和系统文本观感；旧配置里若仍保存
  `Source Han Sans`，加载设置时会自动迁移到这条系统字体链
- 若要启用嵌入式 Source Han Sans VF 字体，请将
  `SourceHanSans-VF.otf`（约 32 MB）手动放置到
  `src/AgentOrchestrator.App/Assets/Fonts/`，然后在
  `设置 / 外观 / UI 字体` 中填入 `Source Han Sans`。该目录
  已被 `.gitignore` 排除，避免把可选的大体积 OT 直接签入仓库

## Binding And Styling

- Compiled bindings are enabled by default
- Every view and data template should set `x:DataType`
- Global style tokens live in `App.axaml`
- `TextBox` uses the chat input style
- `TextBlock.code-text` is the code style (12px Cascadia Code,
  `LineHeight=22`)
- `TextBox.tool-code-viewer` is the dedicated style for the
  read-only `TextBox` inside `ReadOnlyCodeBlock` (12px Cascadia Code,
  `LineHeight=22`, transparent background, no padding)
- `TextBlock.assistant-text` and `TextBlock.thinking-body-text` are
  chat content styles; both use `LineHeight=22` so the breathing room
  matches the code-block typography
- `Border.collapsible-block-body` is the shared body container used
  by `CollapsibleBlockControl` for Thinking / Tool / Task blocks;
  capped at `MaxHeight=300` with `ClipToBounds=True`, and the
  Thinking body wraps its Markdown output in an internal
  `ScrollViewer` so long reasoning scrolls inside the same envelope
- Sidebar styles live under `Button.sidebar-*` and
  `TextBlock.sidebar-*` classes (see `App.axaml`)
- Sidebar task-graph rows intentionally reuse the same selected / hover
  row language as chat session rows so one task graph behaves like one
  chat entry from the user's perspective
- Per-row hover-revealed action buttons: `sidebar-row-action-btn` and
  `sidebar-section-action-btn`
- Project session list paging buttons: `sidebar-session-page-action`
- Session rows show at most one compact runtime badge on the left:
  spinner for streaming, blue dot for completed-but-unviewed, and keep the
  `...` action hidden until hover
- "..." menu items: `sidebar-menu-item`

## CLI Tool

- `AgentOrchestrator.Cli` exercises the same `IAgentGateway` +
  `ISidebarRepository` for headless verification
- Commands: `health`, `list-sessions`, `new-session`, `send`, `messages`,
  `sidebar-list`, `verify`, `orchestration open` (prints the
  `dotnet run --project src/AgentOrchestrator.App -- --open-orchestration`
  hint), `orchestration help`
- Settings overlay: `appsettings.json` then env vars
  `AO_HOST` / `AO_PORT` / `AO_USERNAME` / `AO_PASSWORD`
