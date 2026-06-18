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
- Thinking / Tool / Task 折叠块在运行态只显示前置转圈状态图标，
  不再在标题后追加文字状态标识；完成后恢复静态标题样式
- 聊天区消息列表在滚动条接近底部时会自动跟随最新消息；
  用户主动上拉后不会强制拉回底部，只有接近底部时才继续贴底
- 输入区主按钮会根据当前草稿状态在“发送”和“停止”之间切换；
  发送过程中按钮仍保持可用，这样既能取消当前回复，也能继续把新草稿加入队列
- 当流式回复尚未结束时，新的发送请求会先进入输入区上方的可见队列，
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
  overlay state, search result list, current-project focus, and the
  per-row "..." actions
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
  workspace for task orchestration and subagent activity
- `SettingsWindow.axaml` is a fixed-size settings dialog with a left
  two-level navigation rail. The current top-level groups are `个人`
  and `集成`; `个人 / 常规` shows a placeholder page, and
  `集成 / 服务` shows a service list. `OpenCode` exposes enable state,
  connection status, URL, username, and password; `codex` is present
  as a disabled placeholder entry.

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
