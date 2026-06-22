namespace AgentOrchestrator.App.Services.TaskGraph;

public enum TaskGraphCheckpointKind
{
    NodeCompleted = 0,
    NodeFailed = 1,
    WaitingForInput = 2,
    GraphExpanded = 3,
}
