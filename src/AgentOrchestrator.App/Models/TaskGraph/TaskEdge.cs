namespace AgentOrchestrator.App.Models.TaskGraph;

public sealed class TaskEdge
{
    public string SourceId { get; set; } = string.Empty;

    public string TargetId { get; set; } = string.Empty;
}
