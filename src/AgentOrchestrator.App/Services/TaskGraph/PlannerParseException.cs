using System;

namespace AgentOrchestrator.App.Services.TaskGraph;

public sealed class PlannerParseException : Exception
{
    public PlannerParseException(string message, string rawText)
        : base(message)
    {
        RawText = rawText;
    }

    public string RawText { get; }
}
