# Current Project State

## Application

- Desktop app only
- `net10.0`
- Avalonia 12.0.4
- CommunityToolkit.Mvvm 8.4.1
- DI via Microsoft.Extensions.DependencyInjection 10.0.9
- Local persistence: SQLite via `Microsoft.Data.Sqlite`
- Local DB lives at `{AppContext.BaseDirectory}/Datas/OrchestratorDb.db`

## Code Layout

- `Program.cs` boots Avalonia
- `App.axaml.cs` creates the service provider
- `MainWindowViewModel` owns `Chat`, `TaskGraph`, `Sidebar`, `Settings`
  and an `ActiveWorkspace` that switches between `Chat` and `TaskGraph`
- `ChatWorkspaceViewModel` manages messages, attachments, permissions,
  draft input, and the full session lifecycle (new / open / send /
  stream) via `IAgentGateway` and `ISidebarRepository`
- `SidebarViewModel` owns the project + session tree and the search
  filter; wired to `MainWindowViewModel` via events
- `MainWindow.axaml` hosts a 2-column grid (sidebar | active workspace)
- `SettingsWindow.axaml` is a fixed-size settings dialog

## Binding And Styling

- Compiled bindings are enabled by default
- Every view and data template should set `x:DataType`
- Global style tokens live in `App.axaml`
- `TextBox` uses the chat input style
- `TextBlock.code-text` is the code style
- `TextBlock.assistant-text` and `TextBlock.thinking-body-text` are chat content styles
- Sidebar styles live under `Button.sidebar-*` and `TextBlock.sidebar-*`
  classes (see `App.axaml`)

## CLI Tool

- `AgentOrchestrator.Cli` exercises the same `IAgentGateway` +
  `ISidebarRepository` for headless verification
- Commands: `health`, `list-sessions`, `new-session`, `send`, `messages`,
  `sidebar-list`, `verify`
- Settings overlay: `appsettings.json` then env vars
  `AO_HOST` / `AO_PORT` / `AO_USERNAME` / `AO_PASSWORD`
