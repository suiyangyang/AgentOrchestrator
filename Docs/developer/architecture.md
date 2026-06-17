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
- `MainWindow` hosts a 2-column grid: `SidebarControl` (240px) + a
  `ContentControl` bound to `MainWindowViewModel.ActiveWorkspace`
- `ActiveWorkspace` switches between `ChatWorkspaceViewModel` and
  `TaskGraphWorkspaceViewModel`. Workspace VMs are resolved to their
  control via **explicit DataTemplates** in `App.axaml` — the
  `ViewLocator`'s "ViewModel" → "View" rename does not match the
  project's "Control" naming convention, so the templates are
  declared by hand to keep the rule out of the way.
- `SidebarControl` is the project + session tree, the search box, the
  新对话 / 任务编排 buttons, and the per-row hover-revealed actions
- `ChatWorkspaceControl` is the main chat surface (message list + composer)
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
  ToolState into the chat-block vocabulary, and routes SSE events into
  streaming `ChatStreamChunk` envelopes
- The CLI (`AgentOrchestrator.Cli`) reuses the same services for
  headless verification

## Current Services

- `MarkdownRenderer` handles Markdown rendering for chat content
- `ISidebarRepository` / `SqliteSidebarRepository` (P1)
- `IAgentGateway` / `OpenCodeAgentGateway` / `NotImplementedAgentGateway` (P2-P3)
- `DataPathProvider` resolves `.exe/Datas/OrchestratorDb.db`
- `IAppSettingsService` (extended with `LastProjectId`)
- `DialogHost` provides code-only Confirm/Input Windows used by the
  sidebar rename + remove flows
