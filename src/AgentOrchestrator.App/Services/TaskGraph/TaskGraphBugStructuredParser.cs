using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using AgentOrchestrator.App.Models.TaskGraph;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

public sealed record BugDecisionStructuredResult(
    string Decision,
    string Reason,
    IReadOnlyList<string> MissingInfo
)
{
    public bool RequiresMoreInfo => string.Equals(Decision, "NeedMoreInfo", StringComparison.OrdinalIgnoreCase);
}

public sealed record BugReviewStructuredResult(
    bool Resolved,
    int Reliability,
    string Reason,
    string Verification
);

public sealed record BugReportRowStructuredResult(
    string ProblemDescription,
    bool Resolved,
    int? Reliability,
    string Reason,
    string Verification
);

public static class TaskGraphBugStructuredParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static bool TryParseDecision(TaskNode node, out BugDecisionStructuredResult result)
    {
        result = default!;
        if (TryDeserialize(node, out BugDecisionPayload? payload)
            && payload is not null
            && !string.IsNullOrWhiteSpace(payload.Decision))
        {
            var decision = NormalizeDecision(payload.Decision);
            if (decision is not null)
            {
                result = new BugDecisionStructuredResult(
                    decision,
                    payload.Reason?.Trim() ?? string.Empty,
                    payload.MissingInfo?
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim())
                        .ToList()
                    ?? []);
                return true;
            }
        }

        var rawText = $"{node.RawOutput}\n{node.OutputSummary}";
        if (rawText.Contains("NeedMoreInfo", StringComparison.OrdinalIgnoreCase))
        {
            result = new BugDecisionStructuredResult("NeedMoreInfo", node.OutputSummary ?? string.Empty, []);
            return true;
        }

        if (rawText.Contains("EnoughInfo", StringComparison.OrdinalIgnoreCase))
        {
            result = new BugDecisionStructuredResult("EnoughInfo", node.OutputSummary ?? string.Empty, []);
            return true;
        }

        return false;
    }

    public static bool TryParseReview(TaskNode node, out BugReviewStructuredResult result)
    {
        result = default!;
        if (TryDeserialize(node, out BugReviewPayload? payload)
            && payload is not null)
        {
            var reliability = Math.Clamp(payload.Reliability, 1, 10);
            result = new BugReviewStructuredResult(
                payload.Resolved,
                reliability,
                payload.Reason?.Trim() ?? string.Empty,
                payload.Verification?.Trim() ?? string.Empty);
            return true;
        }

        return false;
    }

    public static bool TryBuildReport(TaskGraphModel graph, out IReadOnlyList<BugReportRowStructuredResult> rows)
    {
        var prefixes = graph.Nodes
            .Select(node => GetBugPrefix(node.Id))
            .Where(x => x is not null)
            .Select(x => x!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        if (prefixes.Count == 0)
        {
            rows = [];
            return false;
        }

        var output = new List<BugReportRowStructuredResult>(prefixes.Count);
        foreach (var prefix in prefixes)
        {
            var readNode = graph.Nodes.FirstOrDefault(x => string.Equals(x.Id, $"{prefix}_read", StringComparison.Ordinal));
            var decisionNode = graph.Nodes.FirstOrDefault(x => string.Equals(x.Id, $"{prefix}_decision", StringComparison.Ordinal));
            var reviewNode = graph.Nodes.FirstOrDefault(x => string.Equals(x.Id, $"{prefix}_review", StringComparison.Ordinal));
            var unresolvedNode = graph.Nodes.FirstOrDefault(x => string.Equals(x.Id, $"{prefix}_unresolved", StringComparison.Ordinal));

            var description = ExtractProblemDescription(readNode);
            if (decisionNode is null || !TryParseDecision(decisionNode, out var decision))
            {
                output.Add(new BugReportRowStructuredResult(
                    description,
                    false,
                    null,
                    "未能解析信息充分性判断结果。",
                    "补充 bug 上下文后重新执行判断节点。"));
                continue;
            }

            if (decision.RequiresMoreInfo)
            {
                var missing = decision.MissingInfo.Count == 0
                    ? string.Empty
                    : $" 缺失信息：{string.Join("；", decision.MissingInfo)}";
                output.Add(new BugReportRowStructuredResult(
                    description,
                    false,
                    null,
                    string.IsNullOrWhiteSpace(decision.Reason)
                        ? $"信息不足，已放入未解决列表。{missing}".Trim()
                        : $"{decision.Reason}{missing}",
                    unresolvedNode?.OutputSummary ?? "等待用户补充后重新分析。"));
                continue;
            }

            if (reviewNode is not null && TryParseReview(reviewNode, out var review))
            {
                output.Add(new BugReportRowStructuredResult(
                    description,
                    review.Resolved,
                    review.Reliability,
                    review.Reason,
                    review.Verification));
                continue;
            }

            output.Add(new BugReportRowStructuredResult(
                description,
                false,
                null,
                "已进入修复路径，但未能解析 review 结果。",
                "重新执行 review 节点或人工检查修复结果。"));
        }

        rows = output;
        return true;
    }

    public static string RenderReportMarkdown(IReadOnlyList<BugReportRowStructuredResult> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("| 问题描述 | 已解决 | 可靠性 | 原因 | 验证方案 |");
        builder.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var row in rows)
        {
            builder.Append("| ")
                .Append(EscapeTableCell(row.ProblemDescription))
                .Append(" | ")
                .Append(row.Resolved ? "是" : "否")
                .Append(" | ")
                .Append(row.Reliability?.ToString() ?? "-")
                .Append(" | ")
                .Append(EscapeTableCell(row.Reason))
                .Append(" | ")
                .Append(EscapeTableCell(row.Verification))
                .AppendLine(" |");
        }

        return builder.ToString().TrimEnd();
    }

    public static string RenderReportJson(IReadOnlyList<BugReportRowStructuredResult> rows)
        => JsonSerializer.Serialize(new
        {
            rows = rows.Select(row => new
            {
                problemDescription = row.ProblemDescription,
                resolved = row.Resolved,
                reliability = row.Reliability,
                reason = row.Reason,
                verification = row.Verification,
            }),
        }, SerializerOptions);

    public static void AnnotateNode(TaskNode node)
    {
        if (node.Tags.Contains("BugDecision") && TryParseDecision(node, out var decision))
        {
            node.ResultTags.Add($"Decision:{decision.Decision}");
            if (decision.MissingInfo.Count > 0)
            {
                node.ResultTags.Add($"Missing:{decision.MissingInfo.Count}");
            }
            return;
        }

        if (node.Title.Contains("可靠性", StringComparison.Ordinal) && TryParseReview(node, out var review))
        {
            node.ResultTags.Add(review.Resolved ? "Resolved" : "NotResolved");
            node.ResultTags.Add($"Reliability:{review.Reliability}");
        }
    }

    private static bool TryDeserialize<T>(TaskNode node, out T? value)
    {
        value = default;
        var rawText = !string.IsNullOrWhiteSpace(node.RawOutput)
            ? node.RawOutput
            : node.OutputSummary;
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return false;
        }

        if (!TaskGraphTemplateBuilder.TryExtractJson(rawText!, out var json))
        {
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize<T>(json, SerializerOptions);
            return value is not null;
        }
        catch
        {
            return false;
        }
    }

    private static string? NormalizeDecision(string decision)
    {
        if (string.Equals(decision, "EnoughInfo", StringComparison.OrdinalIgnoreCase))
        {
            return "EnoughInfo";
        }

        if (string.Equals(decision, "NeedMoreInfo", StringComparison.OrdinalIgnoreCase))
        {
            return "NeedMoreInfo";
        }

        return null;
    }

    private static string ExtractProblemDescription(TaskNode? readNode)
    {
        if (readNode is null)
        {
            return "未知问题";
        }

        var source = readNode.Description;
        var separatorIndex = source.IndexOf('：');
        if (separatorIndex >= 0 && separatorIndex < source.Length - 1)
        {
            return source[(separatorIndex + 1)..].Trim();
        }

        separatorIndex = source.IndexOf(':');
        if (separatorIndex >= 0 && separatorIndex < source.Length - 1)
        {
            return source[(separatorIndex + 1)..].Trim();
        }

        return readNode.Title;
    }

    private static string? GetBugPrefix(string nodeId)
    {
        var marker = "_";
        if (!nodeId.StartsWith("bug_", StringComparison.Ordinal))
        {
            return null;
        }

        var lastSeparator = nodeId.LastIndexOf(marker, StringComparison.Ordinal);
        return lastSeparator <= 0 ? null : nodeId[..lastSeparator];
    }

    private static string EscapeTableCell(string text)
        => (text ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", "<br/>", StringComparison.Ordinal);

    private sealed class BugDecisionPayload
    {
        public string? Decision { get; init; }

        public string? Reason { get; init; }

        public string[]? MissingInfo { get; init; }
    }

    private sealed class BugReviewPayload
    {
        public bool Resolved { get; init; }

        public int Reliability { get; init; }

        public string? Reason { get; init; }

        public string? Verification { get; init; }
    }
}
