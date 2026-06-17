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
  `%LOCALAPPDATA%/AgentOrchestrator/appsettings.local.json`

## Code Layout

- `Program.cs` boots Avalonia
- `App.axaml.cs` creates the service provider
- `App.axaml` declares explicit DataTemplates so `MainWindowViewModel`
  → `ChatWorkspaceViewModel` → `ChatWorkspaceControl` resolves
  correctly without depending on the `ViewLocator` convention
- `MainWindowViewModel` owns `Chat`, `TaskGraph`, `Sidebar`, `Settings`
  and an `ActiveWorkspace` that switches between `Chat` and `TaskGraph`.
  It is also the only place that knows about dialogs (folder picker,
  rename, remove-confirm) — the sidebar raises events and the shell
  handles them.
- `MainWindowViewModel` also owns left/right sidebar visible state and
  widths, so the shell can toggle and resize both sidebars without
  leaking layout state into child workspaces
- `ChatWorkspaceViewModel` manages messages, attachments, permissions,
  draft input, header title, subagent activity, and the full session lifecycle (new / open / send /
  stream) via `IAgentGateway` and `ISidebarRepository`
- 输入区主按钮会根据当前草稿状态在“发送”和“停止”之间切换：
  有草稿时继续发送，无草稿且正在流式回复时取消本次发送
- 当流式回复尚未结束时，新的发送请求会先进入输入区上方的可见队列，
  当前回复完成后按顺序自动续发；队列项支持回填到输入框继续编辑或直接移除
- Assistant 流式消息在收尾同步远端历史时，必须以按 `partId`
  增量合并为准，不能用一份可能尚未完全落稳的远端块列表直接整包替换
  当前 `Blocks`，否则会造成前台短暂丢失 `thinking` / `tool`
  折叠块，而重新打开会话后又恢复
- `OpenCodeAgentGateway.SendMessageAsync` 必须在触发
  `prompt_async` 后立即持续消费 SSE 事件，不能等待 HTTP 调用完整结束后再开始读流；
  文本 part 的 `<think>` / `</think>` 需要按累计文本渐进拆块，这样 UI 才能在
  thinking 打开时先创建折叠块，并继续向其中追加内容
- `SidebarViewModel` owns the project + session tree, the search
  overlay state, search result list, current-project focus, and the
  per-row "..." actions
- `MainWindow.axaml` hosts a 3-column shell with resizable left and
  right sidebars; the center column contains a fixed header area that
  spans the workspace and right sidebar top edge, plus the active
  workspace body
- `ChatWorkspaceControl` adds a fixed header strip inside the chat
  workspace for task orchestration and subagent activity
- `SettingsWindow.axaml` is a fixed-size settings dialog

## Binding And Styling

- Compiled bindings are enabled by default
- Every view and data template should set `x:DataType`
- Global style tokens live in `App.axaml`
- `TextBox` uses the chat input style
- `TextBlock.code-text` is the code style
- `TextBlock.assistant-text` and `TextBlock.thinking-body-text` are
  chat content styles
- Sidebar styles live under `Button.sidebar-*` and
  `TextBlock.sidebar-*` classes (see `App.axaml`)
- Per-row hover-revealed action buttons: `sidebar-row-action-btn` and
  `sidebar-section-action-btn`
- "..." menu items: `sidebar-menu-item`

## CLI Tool

- `AgentOrchestrator.Cli` exercises the same `IAgentGateway` +
  `ISidebarRepository` for headless verification
- Commands: `health`, `list-sessions`, `new-session`, `send`, `messages`,
  `sidebar-list`, `verify`
- Settings overlay: `appsettings.json` then env vars
  `AO_HOST` / `AO_PORT` / `AO_USERNAME` / `AO_PASSWORD`
