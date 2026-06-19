namespace AgentOrchestrator.App.ViewModels;

public sealed class BugReportRowViewModel
{
    public BugReportRowViewModel(string problemDescription, string resolvedText, string reliabilityText, string reason, string verification)
    {
        ProblemDescription = problemDescription;
        ResolvedText = resolvedText;
        ReliabilityText = reliabilityText;
        Reason = reason;
        Verification = verification;
    }

    public string ProblemDescription { get; }

    public string ResolvedText { get; }

    public string ReliabilityText { get; }

    public string Reason { get; }

    public string Verification { get; }
}
