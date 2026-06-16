namespace OpenCode.Client.Models;

public sealed record Pty
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Command { get; init; }
    public required IReadOnlyList<string> Args { get; init; }
    public required string Cwd { get; init; }
    public required string Status { get; init; }
    public long Pid { get; init; }
}

public sealed record LspStatus
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Root { get; init; }
    public required string Status { get; init; }
}

public sealed record FormatterStatus
{
    public required string Name { get; init; }
    public required IReadOnlyList<string> Extensions { get; init; }
    public bool Enabled { get; init; }
}

public sealed record HealthResponse
{
    public bool Healthy { get; init; }
    public string? Version { get; init; }
}
