using System.Text.Json;

namespace OpenCode.Client.Models;

// Abstract base for all part types
public abstract record Part;

public sealed record TextPart : Part
{
    public required string Id { get; init; }
    public required string SessionID { get; init; }
    public required string MessageID { get; init; }
    public string Type { get; init; } = "text";
    public required string Text { get; init; }
    public bool? Synthetic { get; init; }
    public bool? Ignored { get; init; }
    public PartTime? Time { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }
}

public sealed record ReasoningPart : Part
{
    public required string Id { get; init; }
    public required string SessionID { get; init; }
    public required string MessageID { get; init; }
    public string Type { get; init; } = "reasoning";
    public required string Text { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }
    public required PartTime Time { get; init; }
}

public sealed record FilePart : Part
{
    public required string Id { get; init; }
    public required string SessionID { get; init; }
    public required string MessageID { get; init; }
    public string Type { get; init; } = "file";
    public required string Mime { get; init; }
    public string? Filename { get; init; }
    public required string Url { get; init; }
    public FilePartSource? Source { get; init; }
}

public sealed record ToolPart : Part
{
    public required string Id { get; init; }
    public required string SessionID { get; init; }
    public required string MessageID { get; init; }
    public string Type { get; init; } = "tool";
    public required string CallID { get; init; }
    public required string Tool { get; init; }
    public required ToolState State { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }
}

public sealed record StepStartPart : Part
{
    public required string Id { get; init; }
    public required string SessionID { get; init; }
    public required string MessageID { get; init; }
    public string Type { get; init; } = "step-start";
    public string? Snapshot { get; init; }
}

public sealed record StepFinishPart : Part
{
    public required string Id { get; init; }
    public required string SessionID { get; init; }
    public required string MessageID { get; init; }
    public string Type { get; init; } = "step-finish";
    public required string Reason { get; init; }
    public string? Snapshot { get; init; }
    public double Cost { get; init; }
    public required MessageTokens Tokens { get; init; }
}

public sealed record SnapshotPart : Part
{
    public required string Id { get; init; }
    public required string SessionID { get; init; }
    public required string MessageID { get; init; }
    public string Type { get; init; } = "snapshot";
    public required string Snapshot { get; init; }
}

public sealed record PatchPart : Part
{
    public required string Id { get; init; }
    public required string SessionID { get; init; }
    public required string MessageID { get; init; }
    public string Type { get; init; } = "patch";
    public required string Hash { get; init; }
    public required IReadOnlyList<string> Files { get; init; }
}

public sealed record AgentPart : Part
{
    public required string Id { get; init; }
    public required string SessionID { get; init; }
    public required string MessageID { get; init; }
    public string Type { get; init; } = "agent";
    public required string Name { get; init; }
    public AgentPartSource? Source { get; init; }
}

public sealed record RetryPart : Part
{
    public required string Id { get; init; }
    public required string SessionID { get; init; }
    public required string MessageID { get; init; }
    public string Type { get; init; } = "retry";
    public long Attempt { get; init; }
    public required ApiError Error { get; init; }
    public required RetryPartTime Time { get; init; }
}

public sealed record CompactionPart : Part
{
    public required string Id { get; init; }
    public required string SessionID { get; init; }
    public required string MessageID { get; init; }
    public string Type { get; init; } = "compaction";
    public bool Auto { get; init; }
}

public sealed record SubtaskPart : Part
{
    public required string Id { get; init; }
    public required string SessionID { get; init; }
    public required string MessageID { get; init; }
    public string Type { get; init; } = "subtask";
    public required string Prompt { get; init; }
    public required string Description { get; init; }
    public required string Agent { get; init; }
}

public sealed record PartTime
{
    public long Start { get; init; }
    public long? End { get; init; }
}

public sealed record RetryPartTime
{
    public long Created { get; init; }
}

public sealed record AgentPartSource
{
    public required string Value { get; init; }
    public long Start { get; init; }
    public long End { get; init; }
}
