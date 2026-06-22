namespace AgentOrchestrator.App.Models.TaskGraph;

public enum GraphContinueDecisionKind
{
    Continue = 0,
    Pause = 1,
    Cancel = 2,
    RetryFailed = 3,
    SkipNode = 4,
    Summarize = 5,
}

public sealed record GraphContinueDecision(
    GraphContinueDecisionKind Kind,
    string? TargetNodeId = null,
    string? Comment = null);
