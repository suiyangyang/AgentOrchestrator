using System.Text.Json;

namespace OpenCode.Client.Models;

// ToolState discriminated union
public abstract record ToolState;

public sealed record ToolStatePending : ToolState
{
    public string Status { get; init; } = "pending";
    public required IReadOnlyDictionary<string, JsonElement> Input { get; init; }
    public required string Raw { get; init; }
}

public sealed record ToolStateRunning : ToolState
{
    public string Status { get; init; } = "running";
    public required IReadOnlyDictionary<string, JsonElement> Input { get; init; }
    public string? Title { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }
    public required ToolStateRunningTime Time { get; init; }
}

public sealed record ToolStateCompleted : ToolState
{
    public string Status { get; init; } = "completed";
    public required IReadOnlyDictionary<string, JsonElement> Input { get; init; }
    public required string Output { get; init; }
    public required string Title { get; init; }
    public required IReadOnlyDictionary<string, JsonElement> Metadata { get; init; }
    public required ToolStateCompletedTime Time { get; init; }
    public IReadOnlyList<FilePart>? Attachments { get; init; }
}

public sealed record ToolStateError : ToolState
{
    public string Status { get; init; } = "error";
    public required IReadOnlyDictionary<string, JsonElement> Input { get; init; }
    public required string Error { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }
    public required ToolStateErrorTime Time { get; init; }
}

public sealed record ToolStateUnknown : ToolState
{
    public required string Status { get; init; }
    public JsonElement Raw { get; init; }
}

public sealed record ToolStateRunningTime
{
    public long Start { get; init; }
}

public sealed record ToolStateCompletedTime
{
    public long Start { get; init; }
    public long End { get; init; }
    public long? Compacted { get; init; }
}

public sealed record ToolStateErrorTime
{
    public long Start { get; init; }
    public long End { get; init; }
}
