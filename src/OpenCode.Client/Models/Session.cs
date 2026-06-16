namespace OpenCode.Client.Models;

public sealed record Session
{
    public required string Id { get; init; }
    public required string ProjectID { get; init; }
    public required string Directory { get; init; }
    public string? ParentID { get; init; }
    public SessionSummaryInfo? Summary { get; init; }
    public SessionShareInfo? Share { get; init; }
    public required string Title { get; init; }
    public required string Version { get; init; }
    public required SessionTime Time { get; init; }
    public SessionRevertInfo? Revert { get; init; }
}

public sealed record SessionSummaryInfo
{
    public long Additions { get; init; }
    public long Deletions { get; init; }
    public long Files { get; init; }
    public IReadOnlyList<FileDiff>? Diffs { get; init; }
}

public sealed record SessionShareInfo
{
    public required string Url { get; init; }
}

public sealed record SessionTime
{
    public long Created { get; init; }
    public long Updated { get; init; }
    public long? Compacting { get; init; }
}

public sealed record SessionRevertInfo
{
    public required string MessageID { get; init; }
    public string? PartID { get; init; }
    public string? Snapshot { get; init; }
    public string? Diff { get; init; }
}

// SessionStatus discriminated union
public abstract record SessionStatus;

public sealed record SessionStatusIdle : SessionStatus
{
    public string Type { get; init; } = "idle";
}

public sealed record SessionStatusRetry : SessionStatus
{
    public string Type { get; init; } = "retry";
    public long Attempt { get; init; }
    public required string Message { get; init; }
    public long Next { get; init; }
}

public sealed record SessionStatusBusy : SessionStatus
{
    public string Type { get; init; } = "busy";
}
