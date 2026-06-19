using System;
using System.Collections.Generic;
using System.Linq;
using AgentOrchestrator.App.Models.TaskGraph;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

public static class TaskGraphFactory
{
    public static TaskGraphModel FromPlannerSchema(
        PlannerSchema schema,
        TaskGraphMode mode,
        string? sourceContent,
        string? sourceFilePath)
    {
        if (schema.Nodes.Count == 0)
        {
            throw new TaskGraphValidationException("未能生成任何任务节点。");
        }

        var graph = new TaskGraphModel
        {
            Name = string.IsNullOrWhiteSpace(schema.Title) ? "未命名编排" : schema.Title.Trim(),
            Mode = mode,
            TemplateKind = TaskGraphTemplateKind.Custom,
            SourceContent = sourceContent,
            SourceFilePath = sourceFilePath,
            ExecutionState = TaskGraphExecutionState.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nodeSchema in schema.Nodes)
        {
            var id = string.IsNullOrWhiteSpace(nodeSchema.Id)
                ? $"n{usedIds.Count + 1}"
                : nodeSchema.Id.Trim();
            if (!usedIds.Add(id))
            {
                id = $"{id}_{usedIds.Count + 1}";
                usedIds.Add(id);
            }

            var kind = Enum.TryParse<TaskNodeKind>(nodeSchema.Kind, ignoreCase: true, out var parsedKind)
                ? parsedKind
                : TaskNodeKind.Execute;

            var node = new TaskNode
            {
                Id = id,
                Title = string.IsNullOrWhiteSpace(nodeSchema.Title) ? $"任务 {graph.Nodes.Count + 1}" : nodeSchema.Title.Trim(),
                Description = nodeSchema.Description?.Trim() ?? string.Empty,
                Kind = kind,
                Status = TaskNodeStatus.Pending,
                Prompt = BuildPrompt(nodeSchema.Title, nodeSchema.Description, kind),
            };

            foreach (var dep in nodeSchema.DependsOn.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal))
            {
                node.DependsOn.Add(dep.Trim());
            }

            graph.Nodes.Add(node);
        }

        ApplyLayeredPositions(graph);
        graph.RebuildEdges();
        return graph;
    }

    public static void ApplyLayeredPositions(TaskGraphModel graph)
    {
        var layers = TaskGraphTopology.BuildLayers(graph);
        for (var layerIndex = 0; layerIndex < layers.Count; layerIndex++)
        {
            var layer = layers[layerIndex];
            for (var nodeIndex = 0; nodeIndex < layer.Count; nodeIndex++)
            {
                layer[nodeIndex].Position = new NodePosition(
                    40 + (layerIndex * 280),
                    40 + (nodeIndex * 180));
            }
        }
    }

    public static string BuildPrompt(string? title, string? description, TaskNodeKind kind)
    {
        var safeTitle = string.IsNullOrWhiteSpace(title) ? "未命名任务" : title.Trim();
        var safeDescription = string.IsNullOrWhiteSpace(description)
            ? "请完成该任务，并输出关键结果。"
            : description.Trim();
        return $"""
            你正在执行 TaskGraph 节点。

            节点标题: {safeTitle}
            节点类型: {kind}

            任务说明:
            {safeDescription}

            请直接完成任务，并在最后给出简洁的结果总结。
            """;
    }
}
