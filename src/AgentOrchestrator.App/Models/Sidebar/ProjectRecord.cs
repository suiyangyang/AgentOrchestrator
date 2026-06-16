namespace AgentOrchestrator.App.Models.Sidebar;

/// <summary>
/// A local project entry. A project is just a local working directory
/// that is also forwarded to the Agent as its working directory.
/// </summary>
public sealed record ProjectRecord(
    string Id,
    string Name,
    string Directory,
    long CreatedAt
);
