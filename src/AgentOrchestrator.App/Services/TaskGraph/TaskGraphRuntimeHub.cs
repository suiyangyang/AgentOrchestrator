using System;

namespace AgentOrchestrator.App.Services.TaskGraph;

public sealed class TaskGraphRuntimeHub : ITaskGraphRuntimeHub
{
    public event EventHandler<TaskGraphChunkEventArgs>? ChunkReceived;

    public event EventHandler<TaskGraphNodeEventArgs>? NodeChanged;

    public void PublishChunk(string graphId, string nodeId, Services.Agent.ChatStreamChunk chunk)
        => ChunkReceived?.Invoke(this, new TaskGraphChunkEventArgs(graphId, nodeId, chunk));

    public void PublishNodeChanged(string graphId, string nodeId)
        => NodeChanged?.Invoke(this, new TaskGraphNodeEventArgs(graphId, nodeId));
}
