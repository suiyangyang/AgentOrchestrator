using System;
using System.Collections.Generic;
using System.Linq;
using AgentOrchestrator.App.Models.TaskGraph;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

public static class TaskGraphTopology
{
    public static IReadOnlyList<IReadOnlyList<TaskNode>> BuildLayers(TaskGraphModel graph)
    {
        var orderedIds = TopologicalSort(graph);
        var byId = graph.Nodes.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var levelById = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var nodeId in orderedIds)
        {
            var node = byId[nodeId];
            var level = 0;
            foreach (var dep in node.DependsOn)
            {
                if (levelById.TryGetValue(dep, out var depLevel))
                {
                    level = Math.Max(level, depLevel + 1);
                }
            }

            levelById[nodeId] = level;
        }

        return levelById
            .GroupBy(x => x.Value)
            .OrderBy(x => x.Key)
            .Select(group => (IReadOnlyList<TaskNode>)group
                .Select(x => byId[x.Key])
                .ToList())
            .ToList();
    }

    public static IReadOnlyList<string> TopologicalSort(TaskGraphModel graph)
    {
        var byId = graph.Nodes.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var inDegree = graph.Nodes.ToDictionary(x => x.Id, _ => 0, StringComparer.Ordinal);
        var outgoing = graph.Nodes.ToDictionary(x => x.Id, _ => new List<string>(), StringComparer.Ordinal);

        foreach (var node in graph.Nodes)
        {
            foreach (var dep in node.DependsOn.Distinct(StringComparer.Ordinal))
            {
                if (!byId.ContainsKey(dep))
                {
                    throw new TaskGraphValidationException($"节点 “{node.Title}” 依赖了不存在的节点: {dep}");
                }

                inDegree[node.Id]++;
                outgoing[dep].Add(node.Id);
            }
        }

        var queue = new Queue<string>(inDegree
            .Where(x => x.Value == 0)
            .Select(x => x.Key));
        var ordered = new List<string>(graph.Nodes.Count);

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            ordered.Add(nodeId);
            foreach (var target in outgoing[nodeId])
            {
                inDegree[target]--;
                if (inDegree[target] == 0)
                {
                    queue.Enqueue(target);
                }
            }
        }

        if (ordered.Count != graph.Nodes.Count)
        {
            throw new TaskGraphValidationException("任务编排存在循环依赖，无法执行。");
        }

        return ordered;
    }

    public static IReadOnlySet<string> GetDescendantIds(TaskGraphModel graph, string nodeId)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var lookup = graph.Nodes
            .SelectMany(node => node.DependsOn.Select(dep => (dep, node.Id)))
            .GroupBy(x => x.dep, x => x.Id)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.Ordinal);

        var queue = new Queue<string>();
        queue.Enqueue(nodeId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!lookup.TryGetValue(current, out var children))
            {
                continue;
            }

            foreach (var child in children)
            {
                if (result.Add(child))
                {
                    queue.Enqueue(child);
                }
            }
        }

        return result;
    }
}
