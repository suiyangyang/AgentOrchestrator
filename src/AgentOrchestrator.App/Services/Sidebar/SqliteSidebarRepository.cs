using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.Sidebar;
using Microsoft.Data.Sqlite;

namespace AgentOrchestrator.App.Services.Sidebar;

/// <summary>
/// SQLite-backed implementation of <see cref="ISidebarRepository"/>.
/// Uses a single shared connection string and opens short-lived
/// connections per call (Microsoft.Data.Sqlite pools them internally).
/// </summary>
public sealed class SqliteSidebarRepository : ISidebarRepository
{
    private readonly string _connectionString;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public SqliteSidebarRepository() : this(DataPathProvider.DatabaseFile) { }

    public SqliteSidebarRepository(string databasePath)
    {
        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized) return;

            await using var conn = OpenConnection();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous  = NORMAL;
                PRAGMA foreign_keys = ON;

                CREATE TABLE IF NOT EXISTS projects (
                    id          TEXT PRIMARY KEY,
                    name        TEXT NOT NULL,
                    directory   TEXT NOT NULL UNIQUE,
                    created_at  INTEGER NOT NULL
                );

                CREATE TABLE IF NOT EXISTS sessions (
                    session_id        TEXT PRIMARY KEY,
                    agent_session_id  TEXT NOT NULL,
                    title             TEXT NOT NULL,
                    project_id        TEXT,
                    created_at        INTEGER NOT NULL,
                    last_activity_at  INTEGER NOT NULL,
                    viewed_at        INTEGER,
                    FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE SET NULL
                );

                CREATE INDEX IF NOT EXISTS ix_sessions_project  ON sessions(project_id);
                CREATE INDEX IF NOT EXISTS ix_sessions_title    ON sessions(title);
                CREATE INDEX IF NOT EXISTS ix_sessions_created  ON sessions(created_at DESC);
                """;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await EnsureSessionColumnAsync(conn, "last_activity_at", "INTEGER NOT NULL DEFAULT 0", ct).ConfigureAwait(false);
            await EnsureSessionColumnAsync(conn, "viewed_at", "INTEGER", ct).ConfigureAwait(false);

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private async Task EnsureSessionColumnAsync(
        SqliteConnection conn,
        string columnName,
        string columnDefinition,
        CancellationToken ct)
    {
        await using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA table_info(sessions);";
        await using var reader = await pragma.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE sessions ADD COLUMN {columnName} {columnDefinition};";
        await alter.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ── Project ───────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ProjectRecord>> ListProjectsAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var conn = OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, directory, created_at FROM projects ORDER BY created_at DESC;";
        var result = new List<ProjectRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result.Add(new ProjectRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3)));
        }
        return result;
    }

    public async Task<ProjectRecord?> GetProjectAsync(string id, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var conn = OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, directory, created_at FROM projects WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return new ProjectRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3));
        }
        return null;
    }

    public async Task<ProjectRecord> CreateProjectAsync(string name, string directory, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        var record = new ProjectRecord(
            Id: Guid.NewGuid().ToString("N"),
            Name: name,
            Directory: directory,
            CreatedAt: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await using var conn = OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO projects (id, name, directory, created_at)
            VALUES ($id, $name, $directory, $created_at);
            """;
        cmd.Parameters.AddWithValue("$id", record.Id);
        cmd.Parameters.AddWithValue("$name", record.Name);
        cmd.Parameters.AddWithValue("$directory", record.Directory);
        cmd.Parameters.AddWithValue("$created_at", record.CreatedAt);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return record;
    }

    public async Task RenameProjectAsync(string id, string newName, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var conn = OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE projects SET name = $name WHERE id = $id;";
        cmd.Parameters.AddWithValue("$name", newName);
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteProjectAsync(string id, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var conn = OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM projects WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ── Session ───────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SessionRecord>> ListSessionsAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var conn = OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT session_id, agent_session_id, title, project_id, created_at, last_activity_at, viewed_at
            FROM sessions
            ORDER BY created_at DESC;
            """;
        var result = new List<SessionRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result.Add(new SessionRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.IsDBNull(6) ? null : reader.GetInt64(6)));
        }
        return result;
    }

    public async Task<SessionRecord?> GetSessionAsync(string sessionId, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var conn = OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT session_id, agent_session_id, title, project_id, created_at, last_activity_at, viewed_at
            FROM sessions
            WHERE session_id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", sessionId);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return new SessionRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.IsDBNull(6) ? null : reader.GetInt64(6));
        }
        return null;
    }

    public async Task<SessionRecord> CreateSessionAsync(SessionRecord record, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var conn = OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sessions (session_id, agent_session_id, title, project_id, created_at, last_activity_at, viewed_at)
            VALUES ($session_id, $agent_session_id, $title, $project_id, $created_at, $last_activity_at, $viewed_at);
            """;
        cmd.Parameters.AddWithValue("$session_id", record.SessionId);
        cmd.Parameters.AddWithValue("$agent_session_id", record.AgentSessionId);
        cmd.Parameters.AddWithValue("$title", record.Title);
        cmd.Parameters.AddWithValue("$project_id", (object?)record.ProjectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created_at", record.CreatedAt);
        cmd.Parameters.AddWithValue("$last_activity_at", record.LastActivityAt);
        cmd.Parameters.AddWithValue("$viewed_at", (object?)record.ViewedAt ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return record;
    }

    public async Task UpdateSessionAsync(SessionRecord record, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var conn = OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE sessions
            SET agent_session_id = $agent_session_id,
                title            = $title,
                project_id       = $project_id,
                last_activity_at = $last_activity_at,
                viewed_at        = $viewed_at
            WHERE session_id = $session_id;
            """;
        cmd.Parameters.AddWithValue("$session_id", record.SessionId);
        cmd.Parameters.AddWithValue("$agent_session_id", record.AgentSessionId);
        cmd.Parameters.AddWithValue("$title", record.Title);
        cmd.Parameters.AddWithValue("$project_id", (object?)record.ProjectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$last_activity_at", record.LastActivityAt);
        cmd.Parameters.AddWithValue("$viewed_at", (object?)record.ViewedAt ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteSessionAsync(string sessionId, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var conn = OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM sessions WHERE session_id = $id;";
        cmd.Parameters.AddWithValue("$id", sessionId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task MoveSessionToProjectAsync(string sessionId, string? projectId, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var conn = OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sessions SET project_id = $project_id WHERE session_id = $session_id;";
        cmd.Parameters.AddWithValue("$session_id", sessionId);
        cmd.Parameters.AddWithValue("$project_id", (object?)projectId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ── Search ────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SessionRecord>> SearchSessionsByTitleAsync(
        string titleQuery, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var conn = OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT session_id, agent_session_id, title, project_id, created_at, last_activity_at, viewed_at
            FROM sessions
            WHERE title LIKE $kw COLLATE NOCASE
            ORDER BY created_at DESC;
            """;
        cmd.Parameters.AddWithValue("$kw", "%" + titleQuery + "%");
        var result = new List<SessionRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result.Add(new SessionRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.IsDBNull(6) ? null : reader.GetInt64(6)));
        }
        return result;
    }
}
