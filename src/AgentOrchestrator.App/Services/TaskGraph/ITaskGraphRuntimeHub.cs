using System;
using AgentOrchestrator.App.Services.Agent;

namespace AgentOrchestrator.App.Services.TaskGraph;

public interface ITaskGraphRuntimeHub
{
    event EventHandler<TaskGraphChunkEventArgs>? ChunkReceived;

    event EventHandler<TaskGraphNodeEventArgs>? NodeChanged;

    void PublishChunk(string graphId, string nodeId, ChatStreamChunk chunk);

    void PublishNodeChanged(string graphId, string nodeId);
}

public sealed class TaskGraphChunkEventArgs : EventArgs
{
    public TaskGraphChunkEventArgs(string graphId, string nodeId, ChatStreamChunk chunk)
    {
        GraphId = graphId;
        NodeId = nodeId;
        Chunk = chunk;
    }

    public string GraphId { get; }

    public string NodeId { get; }

    public ChatStreamChunk Chunk { get; }
}

public sealed class TaskGraphNodeEventArgs : EventArgs
{
    public TaskGraphNodeEventArgs(string graphId, string nodeId)
    {
        GraphId = graphId;
        NodeId = nodeId;
    }

    public string GraphId { get; }

    public string NodeId { get; }
}
