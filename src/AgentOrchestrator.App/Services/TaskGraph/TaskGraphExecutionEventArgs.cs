using System;
using AgentOrchestrator.App.Models.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

public sealed class TaskGraphExecutionEventArgs : EventArgs
{
    public TaskGraphExecutionEventArgs(string graphId, TaskGraphExecutionState state)
    {
        GraphId = graphId;
        State = state;
    }

    public string GraphId { get; }
    public TaskGraphExecutionState State { get; }
}
