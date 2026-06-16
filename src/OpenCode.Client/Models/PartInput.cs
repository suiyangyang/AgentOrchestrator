namespace OpenCode.Client.Models;

/// <summary>
/// Input parts for sending messages / prompts.
/// </summary>
public abstract record PartInput;

public sealed record TextPartInput : PartInput
{
    public string? Id { get; init; }
    public string Type { get; init; } = "text";
    public required string Text { get; init; }
    public bool? Synthetic { get; init; }
    public bool? Ignored { get; init; }
    public PartTime? Time { get; init; }
    public System.Text.Json.JsonElement? Metadata { get; init; }
}

public sealed record FilePartInput : PartInput
{
    public string? Id { get; init; }
    public string Type { get; init; } = "file";
    public required string Mime { get; init; }
    public string? Filename { get; init; }
    public required string Url { get; init; }
    public FilePartSource? Source { get; init; }
}

public sealed record AgentPartInput : PartInput
{
    public string? Id { get; init; }
    public string Type { get; init; } = "agent";
    public required string Name { get; init; }
    public AgentPartSource? Source { get; init; }
}

public sealed record SubtaskPartInput : PartInput
{
    public string? Id { get; init; }
    public string Type { get; init; } = "subtask";
    public required string Prompt { get; init; }
    public required string Description { get; init; }
    public required string Agent { get; init; }
}
