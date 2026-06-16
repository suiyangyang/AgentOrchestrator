namespace OpenCode.Client.Models;

public sealed record FindMatch
{
    public string? Path { get; init; }
    public string? Lines { get; init; }
    public long LineNumber { get; init; }
    public long AbsoluteOffset { get; init; }
    public IReadOnlyList<FindSubmatch>? Submatches { get; init; }
}

public sealed record FindSubmatch
{
    public string? Match { get; init; }
    public long Start { get; init; }
    public long End { get; init; }
}

public sealed record ToolIds
{
    public IReadOnlyList<string>? BuiltIn { get; init; }
    public IReadOnlyList<string>? Custom { get; init; }
}

public sealed record ToolListItem
{
    public required string Id { get; init; }
    public required string Description { get; init; }
    public System.Text.Json.JsonElement? Parameters { get; init; }
}

public sealed record TuiControlRequest
{
    public required string Path { get; init; }
    public System.Text.Json.JsonElement? Body { get; init; }
}
