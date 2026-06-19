using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentOrchestrator.App.Models.TaskGraph;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

public sealed class TaskGraphDirectParser : ITaskGraphDirectParser
{
    private static readonly Regex BulletRegex = new(@"^\s*[-*]\s*(?:\[[ xX]\]\s*)?(?<title>.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex DependsRegex = new(@"^\s*depends\s+on\s*:\s*(?<deps>.+?)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public TaskGraphModel Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new TaskGraphValidationException("请输入要编排的任务内容。");
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return ParseJson(trimmed);
        }

        return ParseMarkdownList(text);
    }

    private static TaskGraphModel ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("nodes", out var nodesElement) || nodesElement.ValueKind != JsonValueKind.Array)
        {
            throw new TaskGraphValidationException("JSON 输入必须包含 nodes 数组。");
        }

        var title = root.TryGetProperty("title", out var titleElement)
            ? titleElement.GetString()
            : (root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null);

        var nodes = new List<PlannerNodeSchema>();
        foreach (var nodeElement in nodesElement.EnumerateArray())
        {
            var id = nodeElement.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;
            var nodeTitle = nodeElement.TryGetProperty("title", out var titleNodeElement)
                ? titleNodeElement.GetString() ?? string.Empty
                : string.Empty;
            var description = nodeElement.TryGetProperty("description", out var descriptionElement)
                ? descriptionElement.GetString() ?? string.Empty
                : string.Empty;
            var kind = nodeElement.TryGetProperty("kind", out var kindElement)
                ? kindElement.GetString() ?? nameof(TaskNodeKind.Execute)
                : nameof(TaskNodeKind.Execute);
            var deps = new List<string>();
            if (nodeElement.TryGetProperty("dependsOn", out var dependsElement) && dependsElement.ValueKind == JsonValueKind.Array)
            {
                deps.AddRange(dependsElement
                    .EnumerateArray()
                    .Select(x => x.GetString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))!
                    .Select(x => x!.Trim()));
            }

            nodes.Add(new PlannerNodeSchema
            {
                Id = id,
                Title = nodeTitle,
                Description = description,
                Kind = kind,
                DependsOn = deps,
            });
        }

        return TaskGraphFactory.FromPlannerSchema(new PlannerSchema
        {
            Title = string.IsNullOrWhiteSpace(title) ? "直接输入编排" : title.Trim(),
            Nodes = nodes,
        }, TaskGraphMode.Direct, json, null);
    }

    private static TaskGraphModel ParseMarkdownList(string text)
    {
        var definitions = new List<RawNodeDefinition>();
        RawNodeDefinition? current = null;

        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var bulletMatch = BulletRegex.Match(line);
            if (bulletMatch.Success)
            {
                current = new RawNodeDefinition
                {
                    Id = $"n{definitions.Count + 1}",
                    Title = bulletMatch.Groups["title"].Value.Trim(),
                };
                definitions.Add(current);
                continue;
            }

            if (current is null)
            {
                continue;
            }

            var dependsMatch = DependsRegex.Match(line.Trim());
            if (dependsMatch.Success)
            {
                current.DependsOn.AddRange(dependsMatch.Groups["deps"].Value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                continue;
            }

            current.Description = string.IsNullOrWhiteSpace(current.Description)
                ? line.Trim()
                : $"{current.Description}\n{line.Trim()}";
        }

        if (definitions.Count == 0)
        {
            throw new TaskGraphValidationException("未识别到任何任务节点，请使用 Markdown 列表或 JSON。");
        }

        var titleToId = definitions.ToDictionary(x => x.Title, x => x.Id, StringComparer.OrdinalIgnoreCase);
        var nodes = new List<PlannerNodeSchema>();
        foreach (var definition in definitions)
        {
            var resolvedDeps = new List<string>();
            foreach (var dep in definition.DependsOn)
            {
                if (titleToId.TryGetValue(dep, out var depId))
                {
                    resolvedDeps.Add(depId);
                    continue;
                }

                if (definitions.Any(x => string.Equals(x.Id, dep, StringComparison.OrdinalIgnoreCase)))
                {
                    resolvedDeps.Add(dep);
                    continue;
                }

                throw new TaskGraphValidationException($"无法解析依赖 “{dep}”，请确认它与某个节点标题或 id 对应。");
            }

            nodes.Add(new PlannerNodeSchema
            {
                Id = definition.Id,
                Title = definition.Title,
                Description = string.IsNullOrWhiteSpace(definition.Description)
                    ? $"请完成“{definition.Title}”并输出结果。"
                    : definition.Description,
                Kind = nameof(TaskNodeKind.Execute),
                DependsOn = resolvedDeps,
            });
        }

        return TaskGraphFactory.FromPlannerSchema(new PlannerSchema
        {
            Title = "直接输入编排",
            Nodes = nodes,
        }, TaskGraphMode.Direct, text, null);
    }

    private sealed class RawNodeDefinition
    {
        public string Id { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public List<string> DependsOn { get; } = [];
    }
}
