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
  right sidebar
- `ActiveWorkspace` switches between `ChatWorkspaceViewModel` and
  `TaskGraphWorkspaceViewModel`. Workspace VMs are resolved to their
  control via **explicit DataTemplates** in `App.axaml` — the
  `ViewLocator`'s "ViewModel" → "View" rename does not match the
  project's "Control" naming convention, so the templates are
  declared by hand to keep the rule out of the way.
- `SidebarControl` is the project + session tree, the icon+text
  新对话 / 搜索 / 任务编排 buttons, and the per-row hover-revealed actions;
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
- 右侧 `Subagent` 卡片是双层滚动结构：外层面板负责整个右栏滚动，
  卡片内部 `ScrollViewer` 负责 400px 固定高度正文的 Markdown 浏览；
  子会话内容由消息序列聚合成完整 Markdown 摘要，不再只取最后一条预览；
  卡片不显示标题或运行状态文本；当 `Content` 更新时卡片内部自动滚动到底部
- Assistant messages show a left-side loading placeholder immediately
  after send; the placeholder is replaced by normal blocks when the first
  streamed block arrives
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
  `answers[][]`，`ChatWorkspaceViewModel` 维护当前挂起问题状态，`MainWindow` 在标题栏下方渲染
  顶部确认面板，而不是把它塞进消息流或权限菜单
- `TaskGraphWorkspaceControl` is a placeholder until the TaskGraph plan
  ships; v1 shows a fixed "coming soon" page
- `SettingsWindow` is a separate dialog
- Global styles stay in `App.axaml`

## Sidebar Actions

The sidebar surface the following actions; each is wired through a
`RelayCommand` on `SidebarViewModel` and handled by
`MainWindowViewModel` so dialogs and I/O stay in the shell layer:

| Surface                  | Action                  | Effect                                              |
|--------------------------|-------------------------|-----------------------------------------------------|
| Top button "新对话"      | Start a new chat        | Open blank page in chat VM, using current project  |
| Project section header "+" | Add project          | Open folder picker → `Sidebar.AddProjectAsync`      |
| Project section header "..." | (reserved)         | No-op in v1                                         |
| Project row "+"          | New session in project  | Set current project, open blank page                |
| Project row "..."        | More menu               | 置顶项目 / 在资源管理器中打开 / 重命名项目 / 移除    |
| Project session list     | Page sessions           | Show 5 at a time, then expand in batches of 5       |
| Session row "..."        | More menu               | 重命名对话 / 移除                                   |
| 项目 / 对话 section header click | Expand-all / collapse-all | Toggle all project expand states          |
| Project row click        | Expand + focus          | Toggle expand and set current project               |
| Session row click        | Open session            | Switch to chat and load history                     |

The two menus (`...`) are inline popups built in code-behind from a
shared `RowActionPopup` element so we don't need a separate XAML file
per menu.

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
- `ISidebarRepository` is the local SQLite store for project and
  session metadata (no message content is persisted locally)
- `OpenCodeAgentGateway` wraps `OpenCodeClient`, translates Part/Message/
  ToolState into the chat-block vocabulary, routes SSE events into
  streaming `ChatStreamChunk` envelopes, and queries child sessions plus
  session status for subagent activity snapshots
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
- UI 默认字体链为 `Inter, Segoe UI, Microsoft YaHei UI, Microsoft YaHei`；
  `JsonAppSettingsService.Load()` 会把旧配置中的 `Source Han Sans`
  自动迁移到这条系统字体链，避免 Avalonia/Skia 在 Windows 上把常规中文
  文本渲染得过重、发虚
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
- `DialogHost` provides code-only Confirm/Input Windows used by the
  sidebar rename + remove flows
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
