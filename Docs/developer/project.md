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
  draft input, header title, and the full session lifecycle (new / open / send /
  stream) via `IAgentGateway` and `ISidebarRepository`
- `SidebarViewModel` owns the project + session tree, the search
  overlay state, search result list, current-project focus, and the
  per-row "..." actions
- `MainWindow.axaml` hosts a 3-column shell with resizable left and
  right sidebars; the center column contains a fixed header area that
  spans the workspace and right sidebar top edge, plus the active
  workspace body
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
