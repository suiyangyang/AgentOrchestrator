using System.Collections.Generic;

namespace AgentOrchestrator.App.Models.TaskGraph;

public sealed record PlannerSchema
{
    public string Title { get; init; } = string.Empty;

    public IReadOnlyList<PlannerNodeSchema> Nodes { get; init; } = [];
}

public sealed record PlannerNodeSchema
{
    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Kind { get; init; } = nameof(TaskNodeKind.Execute);

    public IReadOnlyList<string> DependsOn { get; init; } = [];
}
