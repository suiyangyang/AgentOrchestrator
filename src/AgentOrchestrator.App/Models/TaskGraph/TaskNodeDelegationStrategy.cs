namespace AgentOrchestrator.App.Models.TaskGraph;

public enum TaskNodeDelegationStrategy
{
    NewSession = 0,
    ChildSession = 1,
    InSessionExecution = 2,
    Inline = 3,
}
