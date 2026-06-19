namespace AgentOrchestrator.App.Models.TaskGraph;

public enum TaskNodeKind
{
    Plan,
    Execute,
    Verify,
    Decision,
    Parallel,
    HumanInput,
}
