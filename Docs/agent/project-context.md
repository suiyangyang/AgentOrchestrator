# Project Context

AgentOrchestrator is a desktop-only Avalonia app on `net10.0`.

Current product focus:

- chat workspace (with real OpenCode send/receive, thinking, tool display;
  collapsed tool headers show the action verb plus a project-relative file
  path parsed from the tool's JSON input; long reasoning scrolls inside a
  300px envelope that matches the read-only code block; long histories open
  from the newest window first and backfill older messages on upward scroll
  with scroll-position compensation)
- sidebar (project + session tree, search, hover-revealed actions,
  "..." menus for pin / open-in-explorer / rename / remove)
- "new session" remembers the last focused project's working
  directory (persisted via `LastProjectId` in `appsettings.local.json`)
- task-graph workspace (saved graphs, template/direct/intent/document generation,
  graph canvas editing, drag-link dependencies, node detail drill-down, bug report export,
  and a focus mode that lets the graph canvas fill the window)
- task-orchestration independent workspace (top-level entry in the title bar; own left
  nav with quick actions + 模板 / 任务图 collapsible groups; right content area shows
  template detail or task-graph summary; `Template` and `TaskGraph` are first-class
  separate assets — templates are generation assets, task graphs are execution entities)
- in-conversation TaskGraph orchestrator: chat workspace owns the active graph
  as the control plane, app executor runs it as the execution plane; checkpoint-based
  pauses publish results back through the runtime hub and a context bridge so the
  current chat session can consume aggregated node summaries as hidden prompt context
- settings window (Personal / General placeholder, Personal / Appearance for
  UI & code font family + size, Integrations / Services with the live
  OpenCode enable/status/URL/username/password card and a codex placeholder)
- compiled bindings
- MVVM with CommunityToolkit.Mvvm
- local SQLite for sidebar metadata
- dependency-inversion: ViewModel ↔ IAgentGateway / ISidebarRepository / ITaskGraphStore / ITaskTemplateStore

Keep this file concise and current.
