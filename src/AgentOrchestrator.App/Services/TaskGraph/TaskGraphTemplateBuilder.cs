using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AgentOrchestrator.App.Models.TaskGraph;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

public static class TaskGraphTemplateBuilder
{
    public static IReadOnlyList<string> ParseInputLines(string rawText)
        => rawText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

    public static TaskGraphModel BuildTaskListGraph(
        string rawText,
        TaskGraphDocumentKind documentKind = TaskGraphDocumentKind.Runtime)
    {
        var lines = ParseInputLines(rawText);
        if (lines.Count == 0)
        {
            throw new TaskGraphValidationException("请先输入任务列表内容。");
        }

        var graph = CreateBaseGraph(
            "长任务列表",
            TaskGraphTemplateKind.TaskList,
            TaskGraphMode.Direct,
            rawText,
            null,
            documentKind);

        TaskNode? previous = null;
        for (var index = 0; index < lines.Count; index++)
        {
            var title = NormalizeLine(lines[index], index + 1);
            var node = CreateNode(
                $"task_{index + 1}",
                title,
                $"按顺序完成任务项：{title}",
                TaskNodeKind.Execute,
                BuildExecutePrompt(title, $"请按顺序完成第 {index + 1} 项任务，并明确输出结果。"));
            if (previous is not null)
            {
                node.DependsOn.Add(previous.Id);
            }

            if (documentKind == TaskGraphDocumentKind.Template)
            {
                node.IsTemplateLocked = true;
            }

            graph.Nodes.Add(node);
            previous = node;
        }

        // ── Template-population for TaskList ──
        if (documentKind == TaskGraphDocumentKind.Template && graph.Nodes.Count > 0)
        {
            graph.TemplateNotes = "适合 1-20 个长链任务，按顺序逐项执行。";
            graph.TemplatePlannerPrompt = "Plan the execution steps in order.";
            graph.TemplateMetadata = new TaskGraphTemplateMetadata
            {
                AllowDynamicExpansion = true,
                FixedNodeIds = [graph.Nodes[0].Id],
                DynamicZones =
                [
                    new DynamicZoneDefinition
                    {
                        Name = "任务执行区",
                        AnchorNodeId = graph.Nodes[0].Id,
                        InsertAfterNodeId = graph.Nodes[0].Id,
                        MaxGeneratedNodeCount = 20,
                        GenerationInstruction = "为每一行任务生成一个串行 Execute 节点，自动连接到上一节点",
                    },
                ],
            };
        }

        TaskGraphFactory.ApplyLayeredPositions(graph);
        graph.RebuildEdges();
        return graph;
    }

    public static TaskGraphModel BuildFeatureDevelopmentGraph(
        string userRequirement,
        TaskGraphDocumentKind documentKind = TaskGraphDocumentKind.Runtime)
    {
        if (string.IsNullOrWhiteSpace(userRequirement))
        {
            throw new TaskGraphValidationException("请先输入功能需求。");
        }

        var graph = CreateBaseGraph(
            "复杂功能开发",
            TaskGraphTemplateKind.FeatureDevelopment,
            TaskGraphMode.Intent,
            userRequirement.Trim(),
            null,
            documentKind);

        var inputNode = CreateNode(
            "feature_requirement",
            "需求输入",
            "记录用户提供的功能需求，作为后续方案与计划生成的原始上下文。",
            TaskNodeKind.Plan,
            BuildExecutePrompt("整理功能需求", $"请将以下需求整理为精炼的开发背景，不要扩写结论，只做输入整理。\n\n{userRequirement.Trim()}"));

        var solutionNode = CreateNode(
            "feature_solution",
            "生成解决方案",
            "分析需求并生成可供用户确认的功能方案、边界和风险。",
            TaskNodeKind.Plan,
            BuildExecutePrompt("生成解决方案", "请给出该功能的解决方案，包含目标、实现思路、范围边界、主要风险与待确认项。"));
        solutionNode.DependsOn.Add(inputNode.Id);

        var confirmNode = CreateNode(
            "feature_confirm",
            "用户确认方案",
            "等待用户确认当前方案，确认后才能继续生成开发计划。",
            TaskNodeKind.HumanInput,
            "等待用户确认当前方案。如果用户未确认，请不要继续推进。");
        confirmNode.DependsOn.Add(solutionNode.Id);
        confirmNode.Tags.Add("AwaitUserConfirmation");

        var planNode = CreateNode(
            "feature_plan",
            "生成开发计划",
            "根据已确认的解决方案生成可执行开发计划，并将执行节点动态加载到当前 TaskGraph。",
            TaskNodeKind.Plan,
            BuildFeaturePlanPrompt());
        planNode.DependsOn.Add(confirmNode.Id);
        planNode.Tags.Add("DynamicPlanExpansion");

        var executeGateNode = CreateNode(
            "feature_execute_gate",
            "开始执行开发计划",
            "作为动态开发任务的统一入口，等待生成的执行节点全部完成后收尾。",
            TaskNodeKind.Verify,
            BuildExecutePrompt("执行收尾", "请汇总动态开发计划的执行结果，确认所有子任务是否完成并输出总结。"));
        executeGateNode.DependsOn.Add(planNode.Id);
        executeGateNode.Tags.Add("DynamicPlanTerminal");

        // ── Template-population for FeatureDevelopment ──
        if (documentKind == TaskGraphDocumentKind.Template)
        {
            inputNode.IsTemplateLocked = true;
            solutionNode.IsTemplateLocked = true;
            confirmNode.IsTemplateLocked = true;
            planNode.IsTemplateLocked = true;
            executeGateNode.IsTemplateLocked = true;
            // plan and gate are fixed anchor/terminal, not the dynamic part.
            planNode.IsDynamicPlaceholder = false;
            executeGateNode.IsDynamicPlaceholder = false;

            graph.TemplateNotes = "先生成方案，等待确认，再动态注入开发计划并执行。适合复杂功能的分阶段开发。";
            graph.TemplatePlannerPrompt = "Plan the feature development phases: requirement, solution, confirmation, plan, execute, verify.";
            graph.TemplateMetadata = new TaskGraphTemplateMetadata
            {
                AllowDynamicExpansion = true,
                FixedNodeIds =
                [
                    inputNode.Id,
                    solutionNode.Id,
                    confirmNode.Id,
                    planNode.Id,
                    executeGateNode.Id,
                ],
                DynamicZones =
                [
                    new DynamicZoneDefinition
                    {
                        Name = "开发执行区",
                        AnchorNodeId = planNode.Id,
                        InsertAfterNodeId = planNode.Id,
                        ConnectToTerminalNodeId = executeGateNode.Id,
                        MaxGeneratedNodeCount = 12,
                        GenerationInstruction = "在 plan 之后、gate 之前插入开发执行节点，每节点类型默认 Execute 或 Verify",
                    },
                ],
            };
        }

        graph.Nodes.Add(inputNode);
        graph.Nodes.Add(solutionNode);
        graph.Nodes.Add(confirmNode);
        graph.Nodes.Add(planNode);
        graph.Nodes.Add(executeGateNode);
        TaskGraphFactory.ApplyLayeredPositions(graph);
        graph.RebuildEdges();
        return graph;
    }

    public static TaskGraphModel BuildBugListGraph(
        string rawText,
        TaskGraphDocumentKind documentKind = TaskGraphDocumentKind.Runtime)
    {
        var bugs = ParseInputLines(rawText);
        if (bugs.Count == 0)
        {
            throw new TaskGraphValidationException("请先输入 bug 列表，每行一个问题。");
        }

        var graph = CreateBaseGraph(
            "Bug 列表处理",
            TaskGraphTemplateKind.BugList,
            TaskGraphMode.Direct,
            rawText,
            null,
            documentKind);

        var reportNode = CreateNode(
            "bug_report",
            "生成 Bug 分析报告",
            "汇总所有 bug 的处理结果，输出问题描述、是否解决、可靠性、原因和验证方案。",
            TaskNodeKind.Verify,
            BuildBugReportPrompt());
        reportNode.Tags.Add("AllowDecisionSkippedDependencies");

        var allNodeIds = new List<string>();
        for (var index = 0; index < bugs.Count; index++)
        {
            var bugLabel = NormalizeLine(bugs[index], index + 1);
            var prefix = $"bug_{index + 1}";

            var readNode = CreateNode(
                $"{prefix}_read",
                $"读取 Bug {index + 1}",
                $"读取并整理 bug 描述：{bugLabel}",
                TaskNodeKind.Plan,
                BuildExecutePrompt($"读取 bug {index + 1}", $"请整理这个 bug 的问题现象、预期行为和关键上下文。\n\nBug 描述：{bugLabel}"));

            var analyzeNode = CreateNode(
                $"{prefix}_analyze",
                $"分析 Bug {index + 1}",
                $"阅读代码和相关文档，分析该 bug 的可能原因：{bugLabel}",
                TaskNodeKind.Plan,
                BuildExecutePrompt($"分析 bug {index + 1}", $"请阅读相关代码与文档，分析 bug 原因、影响范围，以及是否需要用户进一步补充信息。\n\nBug 描述：{bugLabel}"));
            analyzeNode.DependsOn.Add(readNode.Id);

            var decisionNode = CreateNode(
                $"{prefix}_decision",
                $"确认 Bug {index + 1} 信息是否充分",
                $"判断是否需要用户补充更多信息：{bugLabel}",
                TaskNodeKind.Decision,
                BuildBugDecisionPrompt(bugLabel));
            decisionNode.DependsOn.Add(analyzeNode.Id);
            decisionNode.Tags.Add("BugDecision");

            var fixNode = CreateNode(
                $"{prefix}_fix",
                $"修复 Bug {index + 1}",
                $"在信息充分的前提下修复该 bug：{bugLabel}",
                TaskNodeKind.Execute,
                BuildExecutePrompt($"修复 bug {index + 1}", $"请修复该 bug，并说明改动点与影响。\n\nBug 描述：{bugLabel}"));
            fixNode.DependsOn.Add(decisionNode.Id);
            fixNode.Tags.Add("Decision:EnoughInfo");

            var reviewNode = CreateNode(
                $"{prefix}_review",
                $"评估 Bug {index + 1} 可靠性",
                $"自行 review 修复结果，并按 10 分制给出可靠性评分与验证方案：{bugLabel}",
                TaskNodeKind.Verify,
                BuildBugReviewPrompt(bugLabel));
            reviewNode.DependsOn.Add(fixNode.Id);

            var unresolvedNode = CreateNode(
                $"{prefix}_unresolved",
                $"加入未解决列表 {index + 1}",
                $"若信息不足，则把该 bug 放入未解决列表并标记需要用户补充：{bugLabel}",
                TaskNodeKind.HumanInput,
                BuildExecutePrompt($"记录未解决 bug {index + 1}", $"请记录该 bug 当前无法继续处理的原因、缺失信息，以及建议用户补充的内容。\n\nBug 描述：{bugLabel}"));
            unresolvedNode.DependsOn.Add(decisionNode.Id);
            unresolvedNode.Tags.Add("Decision:NeedMoreInfo");
            unresolvedNode.Tags.Add("BugUnresolved");

            graph.Nodes.Add(readNode);
            graph.Nodes.Add(analyzeNode);
            graph.Nodes.Add(decisionNode);
            graph.Nodes.Add(fixNode);
            graph.Nodes.Add(reviewNode);
            graph.Nodes.Add(unresolvedNode);

            allNodeIds.Add(reviewNode.Id);
            allNodeIds.Add(unresolvedNode.Id);
        }

        foreach (var nodeId in allNodeIds)
        {
            reportNode.DependsOn.Add(nodeId);
        }

        // ── Template-population for BugList ──
        if (documentKind == TaskGraphDocumentKind.Template)
        {
            reportNode.IsTemplateLocked = true;
            reportNode.IsDynamicPlaceholder = false;

            graph.TemplateNotes = "逐个分析 bug，自动区分可修复项与待补充项，最后输出报告。";
            graph.TemplatePlannerPrompt = "For each bug, analyze, decide, fix/review or mark unresolved, then summarize.";
            graph.TemplateMetadata = new TaskGraphTemplateMetadata
            {
                AllowDynamicExpansion = true,
                FixedNodeIds = [reportNode.Id],
                DynamicZones =
                [
                    new DynamicZoneDefinition
                    {
                        Name = "Bug 处理区",
                        AnchorNodeId = reportNode.Id,
                        ConnectToTerminalNodeId = reportNode.Id,
                        MaxGeneratedNodeCount = 30,
                        GenerationInstruction = "为每个 bug 生成一个 [read → analyze → decision → (fix → review | unresolved)] 子链，所有子链都连回 bug_report",
                    },
                ],
            };
        }

        graph.Nodes.Add(reportNode);
        TaskGraphFactory.ApplyLayeredPositions(graph);
        graph.RebuildEdges();
        return graph;
    }

    public static bool TryBuildDynamicPlanNodes(TaskNode planNode, out IReadOnlyList<TaskNode> nodes)
    {
        nodes = [];
        if (string.IsNullOrWhiteSpace(planNode.RawOutput))
        {
            return false;
        }

        if (!TryExtractJson(planNode.RawOutput!, out var json))
        {
            return false;
        }

        var parser = new JsonPlanningParser();
        if (!parser.TryParse(json, out var schema) || schema.Nodes.Count == 0)
        {
            return false;
        }

        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var materialized = new List<TaskNode>();
        foreach (var nodeSchema in schema.Nodes)
        {
            var seedId = string.IsNullOrWhiteSpace(nodeSchema.Id) ? $"dyn_{materialized.Count + 1}" : nodeSchema.Id.Trim();
            var id = $"dynamic_{planNode.Id}_{seedId}";
            while (!usedIds.Add(id))
            {
                id = $"{id}_{usedIds.Count + 1}";
            }

            var kind = Enum.TryParse<TaskNodeKind>(nodeSchema.Kind, true, out var parsedKind)
                ? parsedKind
                : TaskNodeKind.Execute;

            var dynamicNode = CreateNode(
                id,
                string.IsNullOrWhiteSpace(nodeSchema.Title) ? $"动态任务 {materialized.Count + 1}" : nodeSchema.Title.Trim(),
                nodeSchema.Description?.Trim() ?? string.Empty,
                kind,
                TaskGraphFactory.BuildPrompt(
                    nodeSchema.Title,
                    nodeSchema.Description,
                    kind));
            dynamicNode.Tags.Add("DynamicNode");
            dynamicNode.Tags.Add(planNode.Id);

            foreach (var dep in nodeSchema.DependsOn.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                dynamicNode.DependsOn.Add($"dynamic_{planNode.Id}_{dep.Trim()}");
            }

            materialized.Add(dynamicNode);
        }

        nodes = materialized;
        return true;
    }

    public static bool TryExtractJson(string text, out string json)
    {
        json = string.Empty;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return false;
        }

        json = text[start..(end + 1)];
        return true;
    }

    /// <summary>
    /// Generic entry point that dispatches to the appropriate template builder
    /// based on <paramref name="kind"/>. For unsupported kinds, returns a
    /// minimal 2-node linear graph suitable for v3 first-version testing.
    /// </summary>
    public static TaskGraphModel Build(
        TaskGraphTemplateKind kind,
        string rawInput = "",
        TaskGraphDocumentKind documentKind = TaskGraphDocumentKind.Runtime)
    {
        return kind switch
        {
            TaskGraphTemplateKind.TaskList => BuildTaskListGraph(rawInput, documentKind),
            TaskGraphTemplateKind.FeatureDevelopment => BuildFeatureDevelopmentGraph(rawInput, documentKind),
            TaskGraphTemplateKind.BugList => BuildBugListGraph(rawInput, documentKind),
            _ => BuildMinimalStub(rawInput, documentKind),
        };
    }

    private static TaskGraphModel BuildMinimalStub(
        string rawInput,
        TaskGraphDocumentKind documentKind = TaskGraphDocumentKind.Runtime)
    {
        var graph = new TaskGraphModel
        {
            Name = "最小模板图",
            DocumentKind = documentKind,
            IsBuiltInTemplate = false,
        };

        var node1 = new TaskNode
        {
            Id = "stub_parse",
            Title = "解析输入",
            Kind = TaskNodeKind.Plan,
            DelegationStrategy = TaskNodeDelegationStrategy.Inline,
            Prompt = string.IsNullOrWhiteSpace(rawInput) ? "无输入" : rawInput,
        };
        var node2 = new TaskNode
        {
            Id = "stub_execute",
            Title = "执行",
            Kind = TaskNodeKind.Execute,
            DelegationStrategy = TaskNodeDelegationStrategy.NewSession,
            Prompt = $"基于以下输入执行：\n{rawInput}",
        };
        node2.DependsOn.Add(node1.Id);

        if (documentKind == TaskGraphDocumentKind.Template)
        {
            node1.IsTemplateLocked = true;
            node2.IsTemplateLocked = true;
            graph.TemplateNotes = "最小模板图（回退）。";
            graph.TemplatePlannerPrompt = "Execute the task based on the input.";
            graph.TemplateMetadata = new TaskGraphTemplateMetadata
            {
                AllowDynamicExpansion = false,
                FixedNodeIds = [node1.Id, node2.Id],
            };
        }

        graph.Nodes.Add(node1);
        graph.Nodes.Add(node2);
        graph.RebuildEdges();
        return graph;
    }

    private static TaskGraphModel CreateBaseGraph(
        string name,
        TaskGraphTemplateKind templateKind,
        TaskGraphMode mode,
        string? sourceContent,
        string? sourceFilePath,
        TaskGraphDocumentKind documentKind)
        => new()
        {
            Name = name,
            TemplateKind = templateKind,
            Mode = mode,
            SourceContent = sourceContent,
            SourceFilePath = sourceFilePath,
            ExecutionState = TaskGraphExecutionState.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            DocumentKind = documentKind,
            IsBuiltInTemplate = documentKind == TaskGraphDocumentKind.Template,
        };

    private static TaskNode CreateNode(
        string id,
        string title,
        string description,
        TaskNodeKind kind,
        string prompt)
    {
        var node = new TaskNode
        {
            Id = id,
            Title = title,
            Description = description,
            Kind = kind,
            Prompt = prompt,
            Status = TaskNodeStatus.Pending,
        };
        node.ResultTags.Clear();
        return node;
    }

    private static string NormalizeLine(string line, int index)
    {
        var trimmed = line.Trim();
        trimmed = trimmed.TrimStart('-', '*', ' ', '\t');
        return string.IsNullOrWhiteSpace(trimmed) ? $"任务 {index}" : trimmed;
    }

    private static string BuildExecutePrompt(string title, string extraInstruction)
        => $"""
             你正在执行 TaskGraph 模板节点。

             节点标题: {title}

             请完成当前节点，并输出清晰结论。
             {extraInstruction}
             """;

    private static string BuildFeaturePlanPrompt()
    {
        var builder = new StringBuilder();
        builder.AppendLine("你正在为一个已确认方案生成开发计划。");
        builder.AppendLine("请仅输出一个 JSON 对象，不要输出 markdown code fence，不要补充解释。");
        builder.AppendLine("JSON schema:");
        builder.AppendLine("{");
        builder.AppendLine("  \"title\": \"开发计划\",");
        builder.AppendLine("  \"nodes\": [");
        builder.AppendLine("    {");
        builder.AppendLine("      \"id\": \"n1\",");
        builder.AppendLine("      \"title\": \"任务标题\",");
        builder.AppendLine("      \"description\": \"任务说明\",");
        builder.AppendLine("      \"kind\": \"Execute\" | \"Plan\" | \"Verify\",");
        builder.AppendLine("      \"dependsOn\": []");
        builder.AppendLine("    }");
        builder.AppendLine("  ]");
        builder.AppendLine("}");
        builder.AppendLine("要求:");
        builder.AppendLine("- 节点必须是可以直接执行的开发任务。");
        builder.AppendLine("- 依赖仅保留真实前置关系。");
        builder.AppendLine("- 最后至少包含一个 Verify 节点。");
        return builder.ToString();
    }

    private static string BuildBugDecisionPrompt(string bugLabel)
        => $$"""
             你正在判断一个 bug 是否具备继续修复的信息。

             Bug 描述: {{bugLabel}}

             请仅输出一个 JSON 对象，不要输出 markdown code fence，不要补充解释。
             JSON schema:
             {
               "decision": "EnoughInfo" | "NeedMoreInfo",
               "reason": "简洁说明判断原因",
               "missingInfo": ["缺失信息 1", "缺失信息 2"]
             }

             规则:
             - 如果信息充分，decision 必须为 EnoughInfo，missingInfo 输出空数组。
             - 如果需要用户补充，decision 必须为 NeedMoreInfo，并把缺失信息拆到 missingInfo 中。
             """;

    private static string BuildBugReviewPrompt(string bugLabel)
        => $$"""
             你正在对 bug 修复结果进行自检。

             Bug 描述: {{bugLabel}}

             请仅输出一个 JSON 对象，不要输出 markdown code fence，不要补充解释。
             JSON schema:
             {
               "resolved": true,
               "reliability": 8,
               "reason": "说明为什么给这个可靠性评分",
               "verification": "建议的验证方案"
             }

             规则:
             - reliability 取值 1-10。
             - resolved 为 false 时，也要说明原因与验证建议。
             """;

    private static string BuildBugReportPrompt()
        => """
             请汇总全部 bug 的处理结果。
             上游节点已经提供了结构化结论，请输出简洁的 Markdown 表格，列必须包含：
             问题描述 | 已解决 | 可靠性 | 原因 | 验证方案
             """;
}
