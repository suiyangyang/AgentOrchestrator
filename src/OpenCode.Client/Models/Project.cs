namespace OpenCode.Client.Models;

public sealed record Project
{
    public required string Id { get; init; }
    public required string Worktree { get; init; }
    public string? VcsDir { get; init; }
    public string? Vcs { get; init; }
    public required ProjectTime Time { get; init; }
}

public sealed record ProjectTime
{
    public long Created { get; init; }
    public long? Initialized { get; init; }
}

public sealed record PathInfo
{
    public required string State { get; init; }
    public required string Worktree { get; init; }
    public required string Directory { get; init; }

    /// <summary>Application config path</summary>
    public required string Config { get; init; }
}

public sealed record VcsInfo
{
    public required string Branch { get; init; }
}
