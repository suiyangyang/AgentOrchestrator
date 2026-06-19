using System;

namespace AgentOrchestrator.App.Services.TaskGraph;

public sealed class TaskGraphValidationException : Exception
{
    public TaskGraphValidationException(string message)
        : base(message)
    {
    }
}
