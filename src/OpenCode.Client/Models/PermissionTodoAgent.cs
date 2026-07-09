using System.Text.Json;

namespace OpenCode.Client.Models;

public sealed record Permission
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public string? Pattern { get; init; }
    public required string SessionID { get; init; }
    public required string MessageID { get; init; }
    public string? CallID { get; init; }
    public required string Title { get; init; }
    public required IReadOnlyDictionary<string, JsonElement> Metadata { get; init; }
    public required PermissionTime Time { get; init; }
}

public sealed record PermissionTime
{
    public long Created { get; init; }
}

public sealed record Todo
{
    public required string Content { get; init; }
    public required string Status { get; init; }
    public required string Priority { get; init; }
    public string? Id { get; init; }
}

public sealed record Command
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Agent { get; init; }
    public string? Model { get; init; }
    public JsonElement? Template { get; init; }
    public bool? Subtask { get; init; }
    public bool? BuiltIn { get; init; }
}

public sealed record Agent
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Mode { get; init; }
    public bool BuiltIn { get; init; }
    public double? TopP { get; init; }
    public double? Temperature { get; init; }
    public string? Color { get; init; }
    public required AgentPermission Permission { get; init; }
    public AgentModelRef? Model { get; init; }
    public string? Prompt { get; init; }
    public required IReadOnlyDictionary<string, bool> Tools { get; init; }
    public required IReadOnlyDictionary<string, JsonElement> Options { get; init; }
    public long? MaxSteps { get; init; }
}

public sealed record AgentPermission
{
    public required string Edit { get; init; }
    public required IReadOnlyDictionary<string, string> Bash { get; init; }
    public string? Webfetch { get; init; }
    public string? DoomLoop { get; init; }
    public string? ExternalDirectory { get; init; }
}

public sealed record AgentModelRef
{
    public required string ModelID { get; init; }
    public required string ProviderID { get; init; }
}
