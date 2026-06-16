namespace AgentOrchestrator.App.Models.Sidebar;

/// <summary>
/// Remote session info returned by an Agent (used to reconcile with the
/// local SQLite store at startup, or to display a snapshot of the remote
/// session list).
/// </summary>
public sealed record RemoteSessionInfo(
    string AgentSessionId,
    string Title,
    long CreatedAt,
    long UpdatedAt
);
