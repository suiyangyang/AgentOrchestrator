using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.Services.Agent;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

public sealed class TaskGraphExecutor : ITaskGraphExecutor, ITaskGraphExecutionController
{
    private const int MaxAttempts = 3;

    private readonly IAgentGateway _agent;
    private readonly ITaskGraphStore _store;
    private readonly INodeOutputInjector _outputInjector;
    private readonly ITaskGraphRuntimeHub _runtimeHub;
    private readonly ConcurrentDictionary<string, ExecutionController> _controllers = new(StringComparer.Ordinal);

    public TaskGraphExecutor(
        IAgentGateway agent,
        ITaskGraphStore store,
        INodeOutputInjector outputInjector,
        ITaskGraphRuntimeHub runtimeHub)
    {
        _agent = agent;
        _store = store;
        _outputInjector = outputInjector;
        _runtimeHub = runtimeHub;
    }

    // ── ITaskGraphExecutor (existing public API, unchanged signatures) ──────

    public async Task ExecuteAsync(TaskGraphModel graph, TaskGraphExecutionRequest request, CancellationToken ct = default)
    {
        var controller = _controllers.GetOrAdd(graph.Id, _ => new ExecutionController());
        await controller.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            controller.CancelRequested = false;
            PrepareGraphForFullRun(graph);
            await RunUntilCheckpointAsync(graph, request, controller, ct).ConfigureAwait(false);
        }
        finally
        {
            controller.Gate.Release();
        }
    }

    public async Task RetryFailedAsync(TaskGraphModel graph, TaskGraphExecutionRequest request, CancellationToken ct = default)
    {
        var controller = _controllers.GetOrAdd(graph.Id, _ => new ExecutionController());
        await controller.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            controller.CancelRequested = false;
            PrepareGraphForRetry(graph);
            await RunUntilCheckpointAsync(graph, request, controller, ct).ConfigureAwait(false);
        }
        finally
        {
            controller.Gate.Release();
        }
    }

    public async Task ContinueAsync(TaskGraphModel graph, TaskGraphExecutionRequest request, CancellationToken ct = default)
    {
        var controller = _controllers.GetOrAdd(graph.Id, _ => new ExecutionController());
        await controller.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            controller.CancelRequested = false;
            PrepareGraphForContinue(graph);
            await RunUntilCheckpointAsync(graph, request, controller, ct).ConfigureAwait(false);
        }
        finally
        {
            controller.Gate.Release();
        }
    }

    public void RequestCancel(string graphId)
    {
        if (_controllers.TryGetValue(graphId, out var controller))
        {
            controller.CancelRequested = true;
            // Signal any pending checkpoint wait so the loop exits promptly.
            controller.PauseSignal?.TrySetResult();
        }
    }

    public async Task ReconcileAsync(TaskGraphModel graph, CancellationToken ct = default)
    {
        if (graph.ExecutionState != TaskGraphExecutionState.Running)
        {
            return;
        }

        var changed = false;
        foreach (var node in graph.Nodes.Where(x => x.Status == TaskNodeStatus.Running))
        {
            changed = true;
            node.Status = TaskNodeStatus.Failed;
            node.LastError = string.IsNullOrWhiteSpace(node.AgentSessionId)
                ? "执行中断: 缺少 session id"
                : "应用中断，需手动重试";
            node.CompletedAt = DateTimeOffset.UtcNow;
        }

        if (changed)
        {
            graph.ExecutionState = TaskGraphExecutionState.Failed;
            graph.ExecutionCompletedAt = DateTimeOffset.UtcNow;
            await PersistAsync(graph, ct).ConfigureAwait(false);
        }
    }

    // ── ITaskGraphExecutionController (new checkpoint-aware API) ────────────

    public async Task StartAsync(TaskGraphModel graph, TaskGraphExecutionRequest request, CancellationToken ct = default)
    {
        await ExecuteAsync(graph, request, ct).ConfigureAwait(false);
    }

    public async Task ResumeAsync(string graphId, GraphContinueDecision decision, CancellationToken ct = default)
    {
        if (!_controllers.TryGetValue(graphId, out var controller))
        {
            throw new InvalidOperationException($"未找到图 '{graphId}' 的执行控制器。请确认图正在执行或已暂停在检查点。");
        }

        controller.PendingDecision = decision;

        // If a checkpoint pause is active, signal it so the loop continues.
        var signal = controller.PauseSignal;
        if (signal != null)
        {
            signal.TrySetResult();
        }
        else
        {
            // No active pause — the decision will be picked up by the next checkpoint.
        }

        // Wait for the pause to drain (the execution loop will reset the signal).
        if (signal != null)
        {
            await signal.Task.ConfigureAwait(false);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task PauseAsync(string graphId, CancellationToken ct = default)
    {
        if (!_controllers.TryGetValue(graphId, out var controller))
        {
            throw new InvalidOperationException($"未找到图 '{graphId}' 的执行控制器。请确认图正在执行。");
        }

        // If already paused at a checkpoint, this is a no-op.
        // If running, set the pause signal so the next checkpoint (or the next
        // iteration through the loop) will stop.
        if (controller.PauseSignal == null)
        {
            controller.PauseSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task CancelAsync(string graphId, CancellationToken ct = default)
    {
        if (!_controllers.TryGetValue(graphId, out var controller))
        {
            throw new InvalidOperationException($"未找到图 '{graphId}' 的执行控制器。请确认图正在执行。");
        }

        controller.CancelRequested = true;
        controller.PauseSignal?.TrySetResult();

        await Task.CompletedTask.ConfigureAwait(false);
    }

    // ── Core execution loop (checkpoint-aware) ──────────────────────────────

    /// <summary>
    /// Runs the graph node-by-node in topological order, pausing at
    /// checkpoints when configured. This is the internal engine behind
    /// <see cref="ExecuteAsync"/>, <see cref="RetryFailedAsync"/>, and
    /// <see cref="ContinueAsync"/>.
    /// </summary>
    private async Task RunUntilCheckpointAsync(
        TaskGraphModel graph,
        TaskGraphExecutionRequest request,
        ExecutionController controller,
        CancellationToken ct)
    {
        graph.ExecutionState = TaskGraphExecutionState.Running;
        graph.ExecutionStartedAt ??= DateTimeOffset.UtcNow;
        graph.ExecutionCompletedAt = null;
        await PersistAsync(graph, ct).ConfigureAwait(false);

        while (true)
        {
            if (controller.CancelRequested)
            {
                break;
            }

            var orderedIds = TaskGraphTopology.TopologicalSort(graph);
            var byId = graph.Nodes.ToDictionary(x => x.Id, StringComparer.Ordinal);

            // Find the next pending node whose dependencies are satisfied.
            TaskNode? nextNode = null;
            foreach (var nodeId in orderedIds)
            {
                var candidate = byId[nodeId];
                if (candidate.Status != TaskNodeStatus.Pending)
                {
                    continue;
                }

                if (candidate.DependsOn.Any(dep => !IsDependencySatisfied(graph, candidate, byId[dep])))
                {
                    candidate.Status = TaskNodeStatus.Skipped;
                    candidate.LastError = "上游节点未成功完成。";
                    continue;
                }

                if (TaskGraphDynamicExpander.ShouldSkipNode(graph, candidate))
                {
                    candidate.Status = TaskNodeStatus.Skipped;
                    candidate.LastError = "根据上游决策结果，该节点无需执行。";
                    candidate.CompletedAt = DateTimeOffset.UtcNow;
                    continue;
                }

                if (TaskGraphDynamicExpander.TryAutoCompleteNode(graph, candidate))
                {
                    continue;
                }

                nextNode = candidate;
                break;
            }

            await PersistAsync(graph, ct).ConfigureAwait(false);

            if (nextNode == null)
            {
                break; // No more work.
            }

            // Handle the WaitingForInput / user-confirmation case.
            if (TaskGraphDynamicExpander.RequiresUserConfirmation(graph, nextNode))
            {
                nextNode.Status = TaskNodeStatus.Completed;
                nextNode.CompletedAt = DateTimeOffset.UtcNow;
                nextNode.OutputSummary ??= "等待用户确认方案并补充结论后继续。";
                graph.ExecutionState = TaskGraphExecutionState.WaitingForInput;
                await PersistAsync(graph, ct).ConfigureAwait(false);

                _runtimeHub.PublishCheckpoint(graph.Id, nextNode.Id, TaskGraphCheckpointKind.WaitingForInput, nextNode.OutputSummary);
                _runtimeHub.PublishExecutionState(graph.Id, TaskGraphExecutionState.WaitingForInput);

                // Wait for resume (same as other checkpoints).
                graph.IsCheckpointPending = true;
                graph.ActiveCheckpointNodeId = nextNode.Id;
                await PersistAsync(graph, ct).ConfigureAwait(false);

                await WaitForResumeAsync(graph, controller, ct).ConfigureAwait(false);

                graph.IsCheckpointPending = false;
                graph.ActiveCheckpointNodeId = null;
                if (controller.PendingDecision != null)
                {
                    await ApplyContinueDecisionAsync(graph, controller.PendingDecision, ct).ConfigureAwait(false);
                    controller.PendingDecision = null;
                }

                await PersistAsync(graph, ct).ConfigureAwait(false);
                continue; // Re-scan for next pending node.
            }

            // Execute the node by its configured strategy.
            var outcome = await ExecuteNodeByStrategyAsync(graph, nextNode, request, controller, ct).ConfigureAwait(false);

            if (!outcome.Success)
            {
                var descendants = TaskGraphTopology.GetDescendantIds(graph, nextNode.Id);
                foreach (var descendantId in descendants)
                {
                    if (byId.TryGetValue(descendantId, out var descendant)
                        && descendant.Status == TaskNodeStatus.Pending)
                    {
                        descendant.Status = TaskNodeStatus.Skipped;
                        descendant.LastError = $"上游节点 \"{nextNode.Title}\" 执行失败。";
                    }
                }

                await PersistAsync(graph, ct).ConfigureAwait(false);
            }

            // Determine whether to enter a checkpoint.
            if (TryEnterCheckpoint(graph, nextNode, outcome, controller, out var checkpointKind, out var checkpointMessage))
            {
                graph.IsCheckpointPending = true;
                graph.ActiveCheckpointNodeId = nextNode.Id;
                graph.ExecutionState = TaskGraphExecutionState.WaitingForInput;
                await PersistAsync(graph, ct).ConfigureAwait(false);

                _runtimeHub.PublishCheckpoint(graph.Id, nextNode.Id, checkpointKind, checkpointMessage);
                _runtimeHub.PublishExecutionState(graph.Id, graph.ExecutionState);

                await WaitForResumeAsync(graph, controller, ct).ConfigureAwait(false);

                graph.IsCheckpointPending = false;
                graph.ActiveCheckpointNodeId = null;
                if (controller.PendingDecision != null)
                {
                    await ApplyContinueDecisionAsync(graph, controller.PendingDecision, ct).ConfigureAwait(false);
                    controller.PendingDecision = null;
                }

                await PersistAsync(graph, ct).ConfigureAwait(false);
            }

            // Handle graph expansion after the node succeeded.
            if (outcome.Success && TaskGraphDynamicExpander.TryExpandAfterNode(graph, nextNode))
            {
                await PersistAsync(graph, ct).ConfigureAwait(false);
            }

            // Loop back — re-scan for the next pending node.
        }

        // Final state.
        if (controller.CancelRequested)
        {
            foreach (var pending in graph.Nodes.Where(x => x.Status == TaskNodeStatus.Pending))
            {
                pending.Status = TaskNodeStatus.Skipped;
                pending.LastError = "执行已取消。";
            }
        }

        graph.ExecutionCompletedAt = DateTimeOffset.UtcNow;
        graph.ExecutionState = controller.CancelRequested
            ? TaskGraphExecutionState.Cancelled
            : graph.Nodes.Any(x => x.Status == TaskNodeStatus.Failed)
                ? TaskGraphExecutionState.Failed
                : TaskGraphExecutionState.Completed;

        await PersistAsync(graph, ct).ConfigureAwait(false);
        _runtimeHub.PublishExecutionState(graph.Id, graph.ExecutionState);
    }

    // ── Strategy dispatch ───────────────────────────────────────────────────

    /// <summary>
    /// Dispatches node execution to the appropriate strategy based on
    /// <see cref="TaskNode.DelegationStrategy"/>.
    /// </summary>
    private async Task<NodeExecutionOutcome> ExecuteNodeByStrategyAsync(
        TaskGraphModel graph,
        TaskNode node,
        TaskGraphExecutionRequest request,
        ExecutionController controller,
        CancellationToken ct)
    {
        return node.DelegationStrategy switch
        {
            TaskNodeDelegationStrategy.NewSession => await ExecuteWithNewSessionAsync(graph, node, request, controller, ct).ConfigureAwait(false),
            TaskNodeDelegationStrategy.ChildSession => await ExecuteWithChildSessionAsync(graph, node, request, ct).ConfigureAwait(false),
            TaskNodeDelegationStrategy.InSessionExecution => await ExecuteInConversationSessionAsync(graph, node, request, ct).ConfigureAwait(false),
            TaskNodeDelegationStrategy.Inline => await ExecuteInlineAsync(graph, node, request, ct).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"未知执行策略: {node.DelegationStrategy}"),
        };
    }

    // ── Execution strategies ────────────────────────────────────────────────

    /// <summary>
    /// Executes the node in a freshly-created Agent session. This is the
    /// original execution path with retry logic (up to <see cref="MaxAttempts"/>
    /// attempts).
    /// </summary>
    private async Task<NodeExecutionOutcome> ExecuteWithNewSessionAsync(
        TaskGraphModel graph,
        TaskNode node,
        TaskGraphExecutionRequest request,
        ExecutionController controller,
        CancellationToken ct)
    {
        while (node.AttemptCount < MaxAttempts && !controller.CancelRequested)
        {
            node.Status = TaskNodeStatus.Running;
            node.StartedAt ??= DateTimeOffset.UtcNow;
            node.CompletedAt = null;
            node.LastError = null;
            await PersistAsync(graph, ct).ConfigureAwait(false);

            try
            {
                var sessionId = await _agent.CreateSessionAsync(
                    new SessionCreateRequest(request.WorkingDirectory, $"TaskGraph-{graph.Id}-{node.Id}"),
                    ct).ConfigureAwait(false);

                node.AgentSessionId = sessionId;
                await PersistAsync(graph, ct).ConfigureAwait(false);

                var prompt = _outputInjector.BuildPrompt(node, graph);
                await foreach (var chunk in _agent.SendMessageAsync(
                                   sessionId,
                                   new ChatRequest(prompt, [], request.Permission, request.Model),
                                   ct).ConfigureAwait(false))
                {
                    _runtimeHub.PublishChunk(graph.Id, node.Id, chunk);
                }

                var messages = await _agent.GetMessagesAsync(sessionId, limit: null, ct).ConfigureAwait(false);
                _outputInjector.PopulateOutput(node, messages);
                PostProcessNodeOutput(graph, node);
                node.Status = TaskNodeStatus.Completed;
                node.CompletedAt = DateTimeOffset.UtcNow;
                await PersistAsync(graph, ct).ConfigureAwait(false);
                return new NodeExecutionOutcome(true, false, node.OutputSummary, null);
            }
            catch (Exception ex)
            {
                node.AttemptCount++;
                node.LastError = ex.Message;
                if (node.AttemptCount >= MaxAttempts || controller.CancelRequested)
                {
                    node.Status = TaskNodeStatus.Failed;
                    node.CompletedAt = DateTimeOffset.UtcNow;
                    await PersistAsync(graph, ct).ConfigureAwait(false);
                    return new NodeExecutionOutcome(false, true, null, ex.Message);
                }

                await PersistAsync(graph, ct).ConfigureAwait(false);
            }
        }

        return new NodeExecutionOutcome(false, true, null, node.LastError);
    }

    /// <summary>
    /// Executes the node in a child session under the parent declared by
    /// <see cref="TaskNode.ParentSessionId"/> (falling back to
    /// <see cref="TaskGraph.ConversationSessionId"/>). If no parent session
    /// id is available, falls back to <see cref="ExecuteWithNewSessionAsync"/>
    /// and logs a debug status message.
    /// </summary>
    private async Task<NodeExecutionOutcome> ExecuteWithChildSessionAsync(
        TaskGraphModel graph,
        TaskNode node,
        TaskGraphExecutionRequest request,
        CancellationToken ct)
    {
        var parentId = node.ParentSessionId ?? graph.ConversationSessionId;
        if (string.IsNullOrWhiteSpace(parentId))
        {
            _runtimeHub.PublishChunk(graph.Id, node.Id,
                new ChatStreamChunk(string.Empty, null, Models.Chat.ChatBlockKind.Text,
                    $"[debug] ChildSession 策略要求父会话 id，但节点 ParentSessionId 和图 ConversationSessionId 均为空，回退为 NewSession。",
                    false));
            return await ExecuteWithNewSessionAsync(graph, node, request, _controllers[graph.Id], ct).ConfigureAwait(false);
        }

        node.Status = TaskNodeStatus.Running;
        node.StartedAt ??= DateTimeOffset.UtcNow;
        node.CompletedAt = null;
        node.LastError = null;
        await PersistAsync(graph, ct).ConfigureAwait(false);

        try
        {
            var sessionId = await _agent.CreateChildSessionAsync(
                parentId,
                new SessionCreateRequest(request.WorkingDirectory, $"TaskGraph-{graph.Id}-{node.Id}"),
                ct).ConfigureAwait(false);

            node.AgentSessionId = sessionId;
            await PersistAsync(graph, ct).ConfigureAwait(false);

            var prompt = _outputInjector.BuildPrompt(node, graph);
            await foreach (var chunk in _agent.SendMessageAsync(
                               sessionId,
                               new ChatRequest(prompt, [], request.Permission, request.Model),
                               ct).ConfigureAwait(false))
            {
                _runtimeHub.PublishChunk(graph.Id, node.Id, chunk);
            }

            var messages = await _agent.GetMessagesAsync(sessionId, limit: null, ct).ConfigureAwait(false);
            _outputInjector.PopulateOutput(node, messages);
            PostProcessNodeOutput(graph, node);
            node.Status = TaskNodeStatus.Completed;
            node.CompletedAt = DateTimeOffset.UtcNow;
            await PersistAsync(graph, ct).ConfigureAwait(false);
            return new NodeExecutionOutcome(true, false, node.OutputSummary, null);
        }
        catch (Exception ex)
        {
            node.Status = TaskNodeStatus.Failed;
            node.LastError = ex.Message;
            node.CompletedAt = DateTimeOffset.UtcNow;
            await PersistAsync(graph, ct).ConfigureAwait(false);
            return new NodeExecutionOutcome(false, true, null, ex.Message);
        }
    }

    /// <summary>
    /// Executes the node directly in the conversation session identified by
    /// <see cref="TaskGraph.ConversationSessionId"/>. No new session is
    /// created. Throws <see cref="InvalidOperationException"/> if no
    /// conversation session id is set.
    /// </summary>
    private async Task<NodeExecutionOutcome> ExecuteInConversationSessionAsync(
        TaskGraphModel graph,
        TaskNode node,
        TaskGraphExecutionRequest request,
        CancellationToken ct)
    {
        var sessionId = graph.ConversationSessionId ?? request.ConversationSessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException(
                $"节点 '{node.Title}' 使用 InSessionExecution 策略，但未绑定 ConversationSessionId。");
        }

        node.Status = TaskNodeStatus.Running;
        node.StartedAt ??= DateTimeOffset.UtcNow;
        node.CompletedAt = null;
        node.LastError = null;
        node.AgentSessionId = sessionId;
        await PersistAsync(graph, ct).ConfigureAwait(false);

        try
        {
            var beforeMessages = await _agent.GetMessagesAsync(sessionId, limit: null, ct).ConfigureAwait(false);
            var prompt = _outputInjector.BuildPrompt(node, graph);
            await foreach (var chunk in _agent.SendMessageAsync(
                               sessionId,
                               new ChatRequest(prompt, [], request.Permission, request.Model),
                               ct).ConfigureAwait(false))
            {
                _runtimeHub.PublishChunk(graph.Id, node.Id, chunk);
            }

            var messages = await GetNewMessagesSinceAsync(sessionId, beforeMessages.Count, ct).ConfigureAwait(false);
            _outputInjector.PopulateOutput(node, messages);
            PostProcessNodeOutput(graph, node);
            node.Status = TaskNodeStatus.Completed;
            node.CompletedAt = DateTimeOffset.UtcNow;
            await PersistAsync(graph, ct).ConfigureAwait(false);
            return new NodeExecutionOutcome(true, false, node.OutputSummary, null);
        }
        catch (Exception ex)
        {
            node.Status = TaskNodeStatus.Failed;
            node.LastError = ex.Message;
            node.CompletedAt = DateTimeOffset.UtcNow;
            await PersistAsync(graph, ct).ConfigureAwait(false);
            return new NodeExecutionOutcome(false, true, null, ex.Message);
        }
    }

    private async Task<NodeExecutionOutcome> ExecuteInlineAsync(
        TaskGraphModel graph,
        TaskNode node,
        TaskGraphExecutionRequest request,
        CancellationToken ct)
    {
        if (request.AllowInlineExecution)
        {
            var sessionId = graph.ConversationSessionId ?? request.ConversationSessionId;
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                return await ExecuteInConversationSessionAsync(graph, node, request, ct).ConfigureAwait(false);
            }
        }

        if (_controllers.TryGetValue(graph.Id, out var controller))
        {
            return await ExecuteWithNewSessionAsync(graph, node, request, controller, ct).ConfigureAwait(false);
        }

        throw new InvalidOperationException($"未找到图 '{graph.Id}' 的执行控制器。");
    }

    // ── Checkpoint helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Determines whether the execution loop should enter a checkpoint after
    /// processing <paramref name="node"/> with the given
    /// <paramref name="outcome"/>.
    /// </summary>
    private bool TryEnterCheckpoint(
        TaskGraphModel graph,
        TaskNode node,
        NodeExecutionOutcome outcome,
        ExecutionController controller,
        out TaskGraphCheckpointKind checkpointKind,
        out string? message)
    {
        checkpointKind = default;
        message = null;

        // External pause requested.
        if (controller.PauseSignal != null)
        {
            checkpointKind = outcome.Success ? TaskGraphCheckpointKind.NodeCompleted : TaskGraphCheckpointKind.NodeFailed;
            message = "外部请求暂停。";
            return true;
        }

        // Node-level checkpoint flag.
        if (node.CheckpointAfterCompletion && outcome.Success)
        {
            checkpointKind = TaskGraphCheckpointKind.NodeCompleted;
            message = outcome.Summary;
            return true;
        }

        // Node failed.
        if (!outcome.Success)
        {
            checkpointKind = TaskGraphCheckpointKind.NodeFailed;
            message = outcome.ErrorMessage;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Suspends execution until <paramref name="controller"/> receives a
    /// resume signal (via <see cref="ResumeAsync"/>) or a cancel request.
    /// </summary>
    private async Task WaitForResumeAsync(
        TaskGraphModel graph,
        ExecutionController controller,
        CancellationToken ct)
    {
        // Ensure a pause signal exists.
        var signal = controller.PauseSignal ?? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        controller.PauseSignal = signal;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var completedTask = await Task.WhenAny(signal.Task, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);

            if (completedTask == signal.Task)
            {
                // Signal was completed by ResumeAsync or CancelAsync.
            }

            // Cancel the infinite delay to release resources.
            cts.Cancel();
        }
        catch (OperationCanceledException)
        {
            // Cancellation has already been handled by setting CancelRequested.
        }
        finally
        {
            // Reset the pause signal so the next checkpoint can create a fresh one.
            controller.PauseSignal = null;
        }
    }

    /// <summary>
    /// Applies a <see cref="GraphContinueDecision"/> to the graph before
    /// resuming execution.
    /// </summary>
    private async Task ApplyContinueDecisionAsync(
        TaskGraphModel graph,
        GraphContinueDecision decision,
        CancellationToken ct)
    {
        switch (decision.Kind)
        {
            case GraphContinueDecisionKind.Continue:
                // Keep going — no graph mutation needed.
                break;

            case GraphContinueDecisionKind.Pause:
                // Re-enter checkpoint wait on the next iteration.
                break;

            case GraphContinueDecisionKind.Cancel:
                if (_controllers.TryGetValue(graph.Id, out var ctrl))
                {
                    ctrl.CancelRequested = true;
                }
                break;

            case GraphContinueDecisionKind.RetryFailed:
                foreach (var node in graph.Nodes.Where(x => x.Status == TaskNodeStatus.Failed))
                {
                    ResetNode(node);
                }
                break;

            case GraphContinueDecisionKind.SkipNode:
                if (!string.IsNullOrWhiteSpace(decision.TargetNodeId))
                {
                    var target = graph.Nodes.FirstOrDefault(x => string.Equals(x.Id, decision.TargetNodeId, StringComparison.Ordinal));
                    if (target != null && target.Status == TaskNodeStatus.Pending)
                    {
                        target.Status = TaskNodeStatus.Skipped;
                        target.LastError = decision.Comment ?? "根据决策跳过该节点。";
                        target.CompletedAt = DateTimeOffset.UtcNow;
                    }
                }
                break;

            case GraphContinueDecisionKind.Summarize:
                // Build a graph-level summary from existing node outputs.
                var completedNodes = graph.Nodes.Where(x => x.Status == TaskNodeStatus.Completed && !string.IsNullOrWhiteSpace(x.OutputSummary)).ToList();
                if (completedNodes.Count > 0)
                {
                    graph.ExecutionState = TaskGraphExecutionState.Completed;
                    graph.ExecutionCompletedAt = DateTimeOffset.UtcNow;
                }
                break;

            default:
                throw new InvalidOperationException($"未知的继续决策类型: {decision.Kind}");
        }

        await PersistAsync(graph, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<Models.Sidebar.RemoteMessage>> GetNewMessagesSinceAsync(
        string sessionId,
        int existingCount,
        CancellationToken ct)
    {
        var allMessages = await _agent.GetMessagesAsync(sessionId, limit: null, ct).ConfigureAwait(false);
        if (existingCount <= 0)
        {
            return allMessages;
        }

        if (existingCount >= allMessages.Count)
        {
            return [];
        }

        return allMessages.Skip(existingCount).ToList();
    }

    // ── Persistence ─────────────────────────────────────────────────────────

    private async Task PersistAsync(TaskGraphModel graph, CancellationToken ct)
    {
        graph.RebuildEdges();
        await _store.SaveAsync(graph, ct).ConfigureAwait(false);
        foreach (var node in graph.Nodes)
        {
            _runtimeHub.PublishNodeChanged(graph.Id, node.Id);
        }
    }

    // ── Graph preparation ───────────────────────────────────────────────────

    private static void PrepareGraphForFullRun(TaskGraphModel graph)
    {
        graph.ExecutionState = TaskGraphExecutionState.Draft;
        graph.ExecutionStartedAt = DateTimeOffset.UtcNow;
        graph.ExecutionCompletedAt = null;
        foreach (var node in graph.Nodes)
        {
            ResetNode(node);
        }
    }

    private static void PrepareGraphForRetry(TaskGraphModel graph)
    {
        var failedNodes = graph.Nodes.Where(x => x.Status == TaskNodeStatus.Failed).ToList();
        if (failedNodes.Count == 0)
        {
            throw new TaskGraphValidationException("当前没有可重试的失败节点。");
        }

        var resetIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var failed in failedNodes)
        {
            resetIds.Add(failed.Id);
            foreach (var descendantId in TaskGraphTopology.GetDescendantIds(graph, failed.Id))
            {
                resetIds.Add(descendantId);
            }
        }

        foreach (var node in graph.Nodes.Where(x => resetIds.Contains(x.Id)))
        {
            ResetNode(node);
        }

        graph.ExecutionState = TaskGraphExecutionState.Draft;
        graph.ExecutionStartedAt = DateTimeOffset.UtcNow;
        graph.ExecutionCompletedAt = null;
    }

    private static void PrepareGraphForContinue(TaskGraphModel graph)
    {
        if (graph.ExecutionState != TaskGraphExecutionState.WaitingForInput)
        {
            throw new TaskGraphValidationException("当前编排不处于等待用户确认状态。");
        }

        graph.ExecutionState = TaskGraphExecutionState.Draft;
        graph.ExecutionCompletedAt = null;

        foreach (var node in graph.Nodes.Where(x => x.Status == TaskNodeStatus.Skipped && string.Equals(x.LastError, "执行已取消。", StringComparison.Ordinal)))
        {
            node.Status = TaskNodeStatus.Pending;
            node.LastError = null;
            node.CompletedAt = null;
        }
    }

    private static void ResetNode(TaskNode node)
    {
        node.Status = TaskNodeStatus.Pending;
        node.AgentSessionId = null;
        node.AttemptCount = 0;
        node.LastError = null;
        node.OutputSummary = null;
        node.RawOutput = null;
        node.StartedAt = null;
        node.CompletedAt = null;
        node.TouchedFiles.Clear();
        node.ResultTags.Clear();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static bool IsDependencySatisfied(TaskGraphModel graph, TaskNode node, TaskNode dependency)
    {
        if (dependency.Status == TaskNodeStatus.Completed)
        {
            return true;
        }

        if (graph.TemplateKind == TaskGraphTemplateKind.BugList
            && node.Tags.Contains("AllowDecisionSkippedDependencies")
            && dependency.Status == TaskNodeStatus.Skipped
            && string.Equals(dependency.LastError, "根据上游决策结果，该节点无需执行。", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static void PostProcessNodeOutput(TaskGraphModel graph, TaskNode node)
    {
        TaskGraphBugStructuredParser.AnnotateNode(node);

        if (graph.TemplateKind == TaskGraphTemplateKind.BugList
            && string.Equals(node.Id, "bug_report", StringComparison.Ordinal)
            && TaskGraphBugStructuredParser.TryBuildReport(graph, out var rows))
        {
            node.OutputSummary = TaskGraphBugStructuredParser.RenderReportMarkdown(rows);
            node.RawOutput = TaskGraphBugStructuredParser.RenderReportJson(rows);
            node.ResultTags.Add($"Rows:{rows.Count}");
        }
    }

    // ── Inner types ─────────────────────────────────────────────────────────

    private sealed record NodeExecutionOutcome(
        bool Success,
        bool NeedsCheckpoint,
        string? Summary,
        string? ErrorMessage);

    private sealed class ExecutionController
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public bool CancelRequested { get; set; }

        /// <summary>
        /// Set when <see cref="TaskGraphExecutor.PauseAsync"/> is called or
        /// when a checkpoint fires and the loop must wait. The execution loop
        /// awaits this signal in <c>WaitForResumeAsync</c>.
        /// </summary>
        public TaskCompletionSource? PauseSignal { get; set; }

        /// <summary>
        /// Set when <see cref="TaskGraphExecutor.ResumeAsync"/> is called.
        /// Consumed by <c>ApplyContinueDecisionAsync</c> on resume.
        /// </summary>
        public GraphContinueDecision? PendingDecision { get; set; }
    }
}
