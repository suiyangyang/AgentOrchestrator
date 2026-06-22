using System;

namespace AgentOrchestrator.App.Services.TaskGraph;

public sealed class TaskGraphCheckpointEventArgs : EventArgs
{
    public string GraphId { get; }
    public string? NodeId { get; }
    public TaskGraphCheckpointKind Kind { get; }
    public string? Message { get; }

    public TaskGraphCheckpointEventArgs(string graphId, string? nodeId, TaskGraphCheckpointKind kind, string? message)
    {
        GraphId = graphId;
        NodeId = nodeId;
        Kind = kind;
        Message = message;
    }
}
