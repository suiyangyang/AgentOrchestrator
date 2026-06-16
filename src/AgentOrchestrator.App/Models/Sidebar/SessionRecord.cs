namespace AgentOrchestrator.App.Models.Sidebar;

/// <summary>
/// Local-side reference to an Agent session. The actual chat content
/// lives in the Agent; only metadata (Title, project attachment, timestamps)
/// is mirrored here.
/// </summary>
public sealed record SessionRecord(
    string SessionId,
    string AgentSessionId,
    string Title,
    string? ProjectId,
    long CreatedAt
);
