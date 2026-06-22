using System;
using AgentOrchestrator.App.Models.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

public sealed class TaskGraphRuntimeHub : ITaskGraphRuntimeHub
{
    public event EventHandler<TaskGraphChunkEventArgs>? ChunkReceived;

    public event EventHandler<TaskGraphNodeEventArgs>? NodeChanged;

    public event EventHandler<TaskGraphCheckpointEventArgs>? CheckpointReached;

    public event EventHandler<TaskGraphExecutionEventArgs>? ExecutionStateChanged;

    public void PublishChunk(string graphId, string nodeId, Services.Agent.ChatStreamChunk chunk)
        => ChunkReceived?.Invoke(this, new TaskGraphChunkEventArgs(graphId, nodeId, chunk));

    public void PublishNodeChanged(string graphId, string nodeId)
        => NodeChanged?.Invoke(this, new TaskGraphNodeEventArgs(graphId, nodeId));

    public void PublishCheckpoint(string graphId, string? nodeId, TaskGraphCheckpointKind kind, string? message)
        => CheckpointReached?.Invoke(this, new TaskGraphCheckpointEventArgs(graphId, nodeId, kind, message));

    public void PublishExecutionState(string graphId, TaskGraphExecutionState state)
        => ExecutionStateChanged?.Invoke(this, new TaskGraphExecutionEventArgs(graphId, state));
}
