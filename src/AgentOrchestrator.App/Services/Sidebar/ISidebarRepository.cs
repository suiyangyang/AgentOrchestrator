using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.Sidebar;

namespace AgentOrchestrator.App.Services.Sidebar;

/// <summary>
/// Repository for the local sidebar metadata (projects + session references).
/// Backed by SQLite in v1. Never stores message content.
/// </summary>
public interface ISidebarRepository
{
    Task InitializeAsync(CancellationToken ct = default);

    // ── Project ──
    Task<IReadOnlyList<ProjectRecord>> ListProjectsAsync(CancellationToken ct = default);
    Task<ProjectRecord?> GetProjectAsync(string id, CancellationToken ct = default);
    Task<ProjectRecord> CreateProjectAsync(string name, string directory, CancellationToken ct = default);
    Task RenameProjectAsync(string id, string newName, CancellationToken ct = default);
    Task DeleteProjectAsync(string id, CancellationToken ct = default);

    // ── Session ──
    Task<IReadOnlyList<SessionRecord>> ListSessionsAsync(CancellationToken ct = default);
    Task<SessionRecord?> GetSessionAsync(string sessionId, CancellationToken ct = default);
    Task<SessionRecord> CreateSessionAsync(SessionRecord record, CancellationToken ct = default);
    Task UpdateSessionAsync(SessionRecord record, CancellationToken ct = default);
    Task DeleteSessionAsync(string sessionId, CancellationToken ct = default);
    Task MoveSessionToProjectAsync(string sessionId, string? projectId, CancellationToken ct = default);

    // ── Search ──
    Task<IReadOnlyList<SessionRecord>> SearchSessionsByTitleAsync(
        string titleQuery, CancellationToken ct = default);
}
