# Project Context

AgentOrchestrator is a desktop-only Avalonia app on `net10.0`.

Current product focus:

- chat workspace (with real OpenCode send/receive, thinking, tool display)
- sidebar (project + session tree, search, hover-revealed actions,
  "..." menus for pin / open-in-explorer / rename / remove)
- "new session" remembers the last focused project's working
  directory (persisted via `LastProjectId` in `appsettings.local.json`)
- task-graph workspace (placeholder, planned)
- settings window
- compiled bindings
- MVVM with CommunityToolkit.Mvvm
- local SQLite for sidebar metadata
- dependency-inversion: ViewModel ↔ IAgentGateway / ISidebarRepository

Keep this file concise and current.
