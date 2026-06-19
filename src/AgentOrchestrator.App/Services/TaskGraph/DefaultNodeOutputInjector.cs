using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AgentOrchestrator.App.Models.Chat;
using AgentOrchestrator.App.Models.Sidebar;
using AgentOrchestrator.App.Models.TaskGraph;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

public sealed class DefaultNodeOutputInjector : INodeOutputInjector
{
    private static readonly Regex FilePathRegex = new(
        @"(?:[A-Za-z]:\\[^\s""']+|(?:src|docs|test|tests|Assets|Views|ViewModels|Services|Models)[\\/][^\s""']+)",
        RegexOptions.Compiled);

    public string BuildPrompt(TaskNode node, TaskGraphModel graph)
    {
        var builder = new StringBuilder();
        if (node.DependsOn.Count > 0)
        {
            builder.AppendLine("Upstream task outputs:");
            foreach (var depId in node.DependsOn)
            {
                var depNode = graph.Nodes.FirstOrDefault(x => x.Id == depId);
                if (depNode is null)
                {
                    continue;
                }

                builder.AppendLine($"- {depNode.Title}: {depNode.OutputSummary ?? "无摘要"}");
                if (depNode.TouchedFiles.Count > 0)
                {
                    builder.AppendLine($"  Files: {string.Join(", ", depNode.TouchedFiles)}");
                }
                if (depNode.ResultTags.Count > 0)
                {
                    builder.AppendLine($"  Tags: {string.Join(", ", depNode.ResultTags)}");
                }
            }

            builder.AppendLine();
            builder.AppendLine("---");
            builder.AppendLine();
        }

        builder.AppendLine(node.Prompt);
        return builder.ToString();
    }

    public void PopulateOutput(TaskNode node, IReadOnlyList<RemoteMessage> messages)
    {
        var assistantBlocks = messages
            .Where(x => x.Role == ChatRole.Assistant)
            .SelectMany(x => x.Blocks)
            .ToList();

        var summarySource = string.Join(
            "\n\n",
            assistantBlocks
                .Select(block => block.Text ?? block.ToolOutput)
                .Where(x => !string.IsNullOrWhiteSpace(x)));

        node.RawOutput = string.IsNullOrWhiteSpace(summarySource)
            ? null
            : summarySource.Trim();

        node.OutputSummary = string.IsNullOrWhiteSpace(summarySource)
            ? "任务已完成。"
            : TrimTo(summarySource.Trim(), 900);

        var touchedFiles = FilePathRegex.Matches(summarySource)
            .Select(x => x.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        node.TouchedFiles.Clear();
        foreach (var path in touchedFiles)
        {
            node.TouchedFiles.Add(path);
        }

        node.ResultTags.Clear();
        node.ResultTags.Add(node.Kind.ToString());
        if (node.TouchedFiles.Count > 0)
        {
            node.ResultTags.Add("Files");
        }
        if (messages.Any(x => x.Blocks.Any(block => block.Kind == ChatBlockKind.Tool || block.Kind == ChatBlockKind.Task)))
        {
            node.ResultTags.Add("Tooling");
        }
    }

    private static string TrimTo(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";
}
