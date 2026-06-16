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
  `TaskGraphWorkspaceViewModel`; the `ViewLocator` resolves the
  matching `*WorkspaceControl` from the VM type
- `SidebarControl` renders the project / session tree, search, and the
  新对话 / 任务编排 buttons
- `ChatWorkspaceControl` is the main chat surface (message list + composer)
- `TaskGraphWorkspaceControl` is a placeholder until the TaskGraph plan
  ships; v1 shows a fixed "coming soon" page
- `SettingsWindow` is a separate dialog
- Global styles stay in `App.axaml`

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
