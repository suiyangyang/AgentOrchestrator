using System;
using System.Collections.Generic;
using System.Linq;
using AgentOrchestrator.App.Models.TaskGraph;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

public static class TaskGraphDynamicExpander
{
    public static bool TryExpandAfterNode(TaskGraphModel graph, TaskNode node)
    {
        if (graph.TemplateKind != TaskGraphTemplateKind.FeatureDevelopment
            || !string.Equals(node.Id, "feature_plan", StringComparison.Ordinal)
            || graph.Nodes.Any(x => x.Tags.Contains("DynamicNode")))
        {
            return false;
        }

        if (!TaskGraphTemplateBuilder.TryBuildDynamicPlanNodes(node, out var dynamicNodes)
            || dynamicNodes.Count == 0)
        {
            return false;
        }

        var executeGateNode = graph.Nodes.FirstOrDefault(x => string.Equals(x.Id, "feature_execute_gate", StringComparison.Ordinal));
        if (executeGateNode is null)
        {
            return false;
        }

        foreach (var dynamicNode in dynamicNodes)
        {
            if (dynamicNode.DependsOn.Count == 0)
            {
                dynamicNode.DependsOn.Add(node.Id);
            }

            graph.Nodes.Add(dynamicNode);
        }

        executeGateNode.DependsOn.Clear();
        foreach (var dynamicNode in dynamicNodes.Where(x => !HasDownstreamInsideSet(x, dynamicNodes)))
        {
            executeGateNode.DependsOn.Add(dynamicNode.Id);
        }

        TaskGraphFactory.ApplyLayeredPositions(graph);
        graph.RebuildEdges();
        return true;
    }

    public static bool RequiresUserConfirmation(TaskGraphModel graph, TaskNode node)
        => graph.TemplateKind == TaskGraphTemplateKind.FeatureDevelopment
           && node.Kind == TaskNodeKind.HumanInput
           && node.Tags.Contains("AwaitUserConfirmation");

    public static bool ShouldSkipNode(TaskGraphModel graph, TaskNode node)
    {
        if (graph.TemplateKind != TaskGraphTemplateKind.BugList || node.Kind != TaskNodeKind.HumanInput && node.Kind != TaskNodeKind.Execute)
        {
            return false;
        }

        var decisionNode = graph.Nodes.FirstOrDefault(x => node.DependsOn.Contains(x.Id));
        if (decisionNode is null)
        {
            return false;
        }

        var isNeedMoreInfo = TaskGraphBugStructuredParser.TryParseDecision(decisionNode, out var decision)
            ? decision.RequiresMoreInfo
            : ContainsDecision(decisionNode, "NeedMoreInfo");
        if (node.Tags.Contains("Decision:NeedMoreInfo"))
        {
            return !isNeedMoreInfo ? true : false;
        }

        if (node.Tags.Contains("Decision:EnoughInfo"))
        {
            return isNeedMoreInfo;
        }

        return false;
    }

    public static bool TryAutoCompleteNode(TaskGraphModel graph, TaskNode node)
    {
        if (graph.TemplateKind == TaskGraphTemplateKind.BugList
            && node.Kind == TaskNodeKind.HumanInput
            && node.Tags.Contains("BugUnresolved"))
        {
            node.Status = TaskNodeStatus.Completed;
            node.CompletedAt = DateTimeOffset.UtcNow;
            node.OutputSummary ??= "该问题信息不足，已加入未解决列表，等待用户补充。";
            node.RawOutput ??= node.OutputSummary;
            return true;
        }

        return false;
    }

    private static bool HasDownstreamInsideSet(TaskNode node, IReadOnlyList<TaskNode> nodes)
    {
        var idSet = nodes.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        return nodes.Any(other => other.DependsOn.Contains(node.Id) && idSet.Contains(other.Id));
    }

    private static bool ContainsDecision(TaskNode decisionNode, string value)
    {
        var text = $"{decisionNode.OutputSummary}\n{decisionNode.RawOutput}";
        return text.Contains($"DECISION: {value}", StringComparison.OrdinalIgnoreCase)
               || text.Contains($"Decision: {value}", StringComparison.OrdinalIgnoreCase);
    }
}
