using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.Services.Agent;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.App.Services.TaskGraph;

public sealed class LlmTaskGraphPlanner : ITaskGraphPlanner
{
    private readonly IAgentGateway _agent;
    private readonly JsonPlanningParser _parser;

    public LlmTaskGraphPlanner(
        IAgentGateway agent,
        JsonPlanningParser parser)
    {
        _agent = agent;
        _parser = parser;
    }

    public Task<TaskGraphModel> CreateFromIntentAsync(
        string userText,
        string workingDirectory,
        string permission,
        string model,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userText))
        {
            throw new TaskGraphValidationException("请输入要编排的任务意图。");
        }

        return RunPlannerAsync(
            BuildPlannerPrompt(userText.Trim(), documentContent: null),
            TaskGraphMode.Intent,
            userText.Trim(),
            null,
            workingDirectory,
            permission,
            model,
            ct);
    }

    public Task<TaskGraphModel> CreateFromDocumentAsync(
        string filePath,
        string documentContent,
        string workingDirectory,
        string permission,
        string model,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(documentContent))
        {
            throw new TaskGraphValidationException("文档内容为空，无法生成编排。");
        }

        var prompt = BuildPlannerPrompt(
            "请基于提供的文档内容生成任务编排。",
            documentContent.Trim());

        return RunPlannerAsync(
            prompt,
            TaskGraphMode.Document,
            documentContent,
            filePath,
            workingDirectory,
            permission,
            model,
            ct);
    }

    private async Task<TaskGraphModel> RunPlannerAsync(
        string initialPrompt,
        TaskGraphMode mode,
        string sourceContent,
        string? sourceFilePath,
        string workingDirectory,
        string permission,
        string model,
        CancellationToken ct)
    {
        var sessionId = await _agent.CreateSessionAsync(
            new SessionCreateRequest(workingDirectory, $"Planner-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}"),
            ct).ConfigureAwait(false);

        try
        {
            var prompt = initialPrompt;
            var rawText = string.Empty;
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                rawText = await SendPromptAsync(sessionId, prompt, permission, model, ct).ConfigureAwait(false);
                if (_parser.TryParse(rawText, out var schema))
                {
                    return TaskGraphFactory.FromPlannerSchema(schema, mode, sourceContent, sourceFilePath);
                }

                if (attempt == 3)
                {
                    break;
                }

                prompt = $$"""
                    你上一次的输出无法解析为严格 JSON。

                    上一次输出:
                    {{rawText}}

                    请仅输出一个 JSON 对象，且必须符合以下要求:
                    - 顶层字段: title, nodes
                    - nodes 中每个元素包含: id, title, description, kind, dependsOn
                    - 输出中不能包含 markdown code fence
                    - 不要解释，不要补充说明
                    """;
            }

            throw new PlannerParseException("无法解析 LLM 输出，已重试 3 次。", rawText);
        }
        finally
        {
            try
            {
                await _agent.DeleteSessionAsync(sessionId, ct).ConfigureAwait(false);
            }
            catch
            {
                // Best effort only.
            }
        }
    }

    private async Task<string> SendPromptAsync(
        string sessionId,
        string prompt,
        string permission,
        string model,
        CancellationToken ct)
    {
        var builder = new StringBuilder();
        await foreach (var chunk in _agent.SendMessageAsync(
                           sessionId,
                           new ChatRequest(prompt, [], permission, model),
                           ct).ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(chunk.Content))
            {
                builder.Append(chunk.Content);
            }
        }

        if (builder.Length > 0)
        {
            return builder.ToString();
        }

        var messages = await _agent.GetMessagesAsync(sessionId, limit: null, ct).ConfigureAwait(false);
        var assistant = messages.LastOrDefault(x => x.Role == Models.Chat.ChatRole.Assistant);
        if (assistant is null)
        {
            return string.Empty;
        }

        return string.Join(
            "\n\n",
            assistant.Blocks
                .Select(block => block.Text ?? block.ToolOutput)
                .Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static string BuildPlannerPrompt(string userText, string? documentContent)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are a task planner. Given a user's request, decompose it into a directed acyclic graph (DAG) of executable tasks.");
        builder.AppendLine();
        builder.AppendLine("Output MUST be a single JSON object with this exact schema:");
        builder.AppendLine("{");
        builder.AppendLine("  \"title\": \"<plan title>\",");
        builder.AppendLine("  \"nodes\": [");
        builder.AppendLine("    {");
        builder.AppendLine("      \"id\": \"<short stable id, e.g. n1>\",");
        builder.AppendLine("      \"title\": \"<task title, max 50 chars>\",");
        builder.AppendLine("      \"description\": \"<what this task does, 1-3 sentences>\",");
        builder.AppendLine("      \"kind\": \"Execute\" | \"Plan\" | \"Verify\" | \"Decision\",");
        builder.AppendLine("      \"dependsOn\": [\"<id>\"]");
        builder.AppendLine("    }");
        builder.AppendLine("  ]");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("Rules:");
        builder.AppendLine("- Each task must be independently executable.");
        builder.AppendLine("- Use dependsOn only for true prerequisites.");
        builder.AppendLine("- Output JSON only. No markdown fences, no explanation.");
        builder.AppendLine();
        builder.AppendLine("User request:");
        builder.AppendLine(userText);

        if (!string.IsNullOrWhiteSpace(documentContent))
        {
            builder.AppendLine();
            builder.AppendLine("Document content:");
            builder.AppendLine(documentContent);
        }

        return builder.ToString();
    }
}
