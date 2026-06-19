namespace AgentOrchestrator.App.Models.TaskGraph;

public enum TaskGraphExecutionState
{
    Draft,
    Running,
    WaitingForInput,
    Completed,
    Failed,
    Cancelled,
}
