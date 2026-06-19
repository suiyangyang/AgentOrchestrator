# Project Context

AgentOrchestrator is a desktop-only Avalonia app on `net10.0`.

Current product focus:

- chat workspace (with real OpenCode send/receive, thinking, tool display;
  collapsed tool headers show the action verb plus a project-relative file
  path parsed from the tool's JSON input; long reasoning scrolls inside a
  300px envelope that matches the read-only code block)
- sidebar (project + session tree, search, hover-revealed actions,
  "..." menus for pin / open-in-explorer / rename / remove)
- "new session" remembers the last focused project's working
  directory (persisted via `LastProjectId` in `appsettings.local.json`)
- task-graph workspace (placeholder, planned)
- settings window (Personal / General placeholder, Personal / Appearance for
  UI & code font family + size, Integrations / Services with the live
  OpenCode enable/status/URL/username/password card and a codex placeholder)
- compiled bindings
- MVVM with CommunityToolkit.Mvvm
- local SQLite for sidebar metadata
- dependency-inversion: ViewModel ↔ IAgentGateway / ISidebarRepository

Keep this file concise and current.
