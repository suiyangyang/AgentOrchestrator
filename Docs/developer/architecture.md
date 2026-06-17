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
  新对话 / 搜索 / 插件 buttons, and the per-row hover-revealed actions
- The left sidebar toggle sits in the custom title bar, and the right
  sidebar toggle sits in the center workspace header
- Search opens as a centered modal overlay with a dimmed backdrop and
  filters the current sessions by title; selecting a result opens that
  session and closes the overlay
- `ChatWorkspaceControl` is the main chat surface (fixed top header strip + message list + composer)
- Assistant messages show a left-side loading placeholder immediately
  after send; the placeholder is replaced by normal blocks when the first
  streamed block arrives
- The composer primary button switches between send and stop based on
  whether the draft box currently has content; when a response is still
  streaming and the draft box is empty, the same button cancels the send
- While a response is streaming, additional composer submissions are
  added to a visible queue above the input and are sent automatically in
  order after the current response finishes
- Streamed chat blocks are created as soon as the first block header is
  detected, then their text/tool body is updated in place as later stream
  chunks arrive
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
  ToolState into the chat-block vocabulary, routes SSE events into
  streaming `ChatStreamChunk` envelopes, and queries child sessions plus
  session status for subagent activity snapshots
- `ChatWorkspaceViewModel` does a final `GetMessagesAsync` reconciliation
  after a send stream completes so tool blocks reflect the server's
  terminal state even when the last SSE tool update was only `running`
  and refreshes subagent activity while the current session is streaming
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
