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

public sealed class TaskGraphExecutor : ITaskGraphExecutor
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

    public async Task ExecuteAsync(TaskGraphModel graph, TaskGraphExecutionRequest request, CancellationToken ct = default)
    {
        var controller = _controllers.GetOrAdd(graph.Id, _ => new ExecutionController());
        await controller.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            controller.CancelRequested = false;
            PrepareGraphForFullRun(graph);
            await RunPendingNodesAsync(graph, request, controller, ct).ConfigureAwait(false);
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
            await RunPendingNodesAsync(graph, request, controller, ct).ConfigureAwait(false);
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
            await RunPendingNodesAsync(graph, request, controller, ct).ConfigureAwait(false);
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

    private async Task RunPendingNodesAsync(
        TaskGraphModel graph,
        TaskGraphExecutionRequest request,
        ExecutionController controller,
        CancellationToken ct)
    {
        TaskGraphTopology.TopologicalSort(graph);
        graph.ExecutionState = TaskGraphExecutionState.Running;
        graph.ExecutionStartedAt ??= DateTimeOffset.UtcNow;
        graph.ExecutionCompletedAt = null;
        await PersistAsync(graph, ct).ConfigureAwait(false);

        var orderedIds = TaskGraphTopology.TopologicalSort(graph);
        var cursor = 0;
        while (cursor < orderedIds.Count)
        {
            var byId = graph.Nodes.ToDictionary(x => x.Id, StringComparer.Ordinal);
            var nodeId = orderedIds[cursor++];
            if (controller.CancelRequested)
            {
                break;
            }

            var node = byId[nodeId];
            if (node.Status != TaskNodeStatus.Pending)
            {
                continue;
            }

            if (node.DependsOn.Any(dep => !IsDependencySatisfied(graph, node, byId[dep])))
            {
                node.Status = TaskNodeStatus.Skipped;
                node.LastError = "上游节点未成功完成。";
                await PersistAsync(graph, ct).ConfigureAwait(false);
                continue;
            }

            if (TaskGraphDynamicExpander.ShouldSkipNode(graph, node))
            {
                node.Status = TaskNodeStatus.Skipped;
                node.LastError = "根据上游决策结果，该节点无需执行。";
                node.CompletedAt = DateTimeOffset.UtcNow;
                await PersistAsync(graph, ct).ConfigureAwait(false);
                continue;
            }

            if (TaskGraphDynamicExpander.TryAutoCompleteNode(graph, node))
            {
                await PersistAsync(graph, ct).ConfigureAwait(false);
                continue;
            }

            if (TaskGraphDynamicExpander.RequiresUserConfirmation(graph, node))
            {
                node.Status = TaskNodeStatus.Completed;
                node.CompletedAt = DateTimeOffset.UtcNow;
                node.OutputSummary ??= "等待用户确认方案并补充结论后继续。";
                graph.ExecutionState = TaskGraphExecutionState.WaitingForInput;
                await PersistAsync(graph, ct).ConfigureAwait(false);
                return;
            }

            var completed = await ExecuteNodeAsync(graph, node, request, controller, ct).ConfigureAwait(false);
            if (!completed)
            {
                var descendants = TaskGraphTopology.GetDescendantIds(graph, node.Id);
                foreach (var descendantId in descendants)
                {
                    var descendant = byId[descendantId];
                    if (descendant.Status == TaskNodeStatus.Pending)
                    {
                        descendant.Status = TaskNodeStatus.Skipped;
                        descendant.LastError = $"上游节点 “{node.Title}” 执行失败。";
                    }
                }

                await PersistAsync(graph, ct).ConfigureAwait(false);
            }

            if (completed && TaskGraphDynamicExpander.TryExpandAfterNode(graph, node))
            {
                orderedIds = TaskGraphTopology.TopologicalSort(graph).ToList();
                byId = graph.Nodes.ToDictionary(x => x.Id, StringComparer.Ordinal);
                await PersistAsync(graph, ct).ConfigureAwait(false);
            }
        }

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
    }

    private async Task<bool> ExecuteNodeAsync(
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
                return true;
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
                    return false;
                }

                await PersistAsync(graph, ct).ConfigureAwait(false);
            }
        }

        return false;
    }

    private async Task PersistAsync(TaskGraphModel graph, CancellationToken ct)
    {
        graph.RebuildEdges();
        await _store.SaveAsync(graph, ct).ConfigureAwait(false);
        foreach (var node in graph.Nodes)
        {
            _runtimeHub.PublishNodeChanged(graph.Id, node.Id);
        }
    }

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

    private sealed class ExecutionController
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public bool CancelRequested { get; set; }
    }
}
