using System.Text.Json;

namespace OpenCode.Client.Models;

public sealed record FileDiff
{
    public required string File { get; init; }
    public required string Before { get; init; }
    public required string After { get; init; }
    public long Additions { get; init; }
    public long Deletions { get; init; }
}

public sealed record UserMessage
{
    public required string Id { get; init; }
    public required string SessionID { get; init; }
    public string Role { get; init; } = "user";
    public required MessageTime Time { get; init; }
    public MessageSummary? Summary { get; init; }
    public required string Agent { get; init; }
    public required ModelRef Model { get; init; }
    public string? System { get; init; }
    public IReadOnlyDictionary<string, bool>? Tools { get; init; }
}

public sealed record AssistantMessage
{
    public required string Id { get; init; }
    public required string SessionID { get; init; }
    public string Role { get; init; } = "assistant";
    public required AssistantMessageTime Time { get; init; }
    public JsonElement? Error { get; init; }
    public required string ParentID { get; init; }
    public required string ModelID { get; init; }
    public required string ProviderID { get; init; }
    public required string Mode { get; init; }
    public required MessagePath Path { get; init; }
    public bool? Summary { get; init; }
    public double Cost { get; init; }
    public required MessageTokens Tokens { get; init; }
    public string? Finish { get; init; }
}

// Message discriminant union base (implemented via custom JsonConverter)
public abstract record Message
{
    // Abstract base - UserMessage and AssistantMessage are sealed subtypes
}

// Concrete message types as wrappers
public sealed record UserMessageWrapper : Message
{
    public required UserMessage Value { get; init; }
}

public sealed record AssistantMessageWrapper : Message
{
    public required AssistantMessage Value { get; init; }
}

public sealed record MessageTime
{
    public long Created { get; init; }
}

public sealed record AssistantMessageTime
{
    public long Created { get; init; }
    public long? Completed { get; init; }
}

public sealed record MessageSummary
{
    public string? Title { get; init; }
    public string? Body { get; init; }
    public required IReadOnlyList<FileDiff> Diffs { get; init; }
}

public sealed record ModelRef
{
    public required string ProviderID { get; init; }
    public required string ModelID { get; init; }
}

public sealed record MessagePath
{
    public required string Cwd { get; init; }
    public required string Root { get; init; }
}

public sealed record MessageTokens
{
    public long Input { get; init; }
    public long Output { get; init; }
    public long Reasoning { get; init; }
    public required TokenCache Cache { get; init; }
}

public sealed record TokenCache
{
    public long Read { get; init; }
    public long Write { get; init; }
}

/// <summary>
/// Represents a message with its parts, as returned by various session message endpoints.
/// </summary>
public sealed record MessageWithParts
{
    public required Message Info { get; init; }
    public required IReadOnlyList<Part> Parts { get; init; }
}
