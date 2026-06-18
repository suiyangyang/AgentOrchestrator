using System.Collections.Generic;
using System.Text.Json;
using AgentOrchestrator.App.Models.Chat;

namespace AgentOrchestrator.App.Models.Sidebar;

/// <summary>
/// One message returned by the Agent. The block kind/text/tool metadata
/// has already been flattened to the chat-block vocabulary, so the
/// ViewModel layer never has to know about Agent-specific DTOs.
/// </summary>
public sealed record RemoteMessage(
    string Id,
    ChatRole Role,
    IReadOnlyList<RemoteBlock> Blocks
);

public sealed record RemoteBlock(
    ChatBlockKind Kind,
    string? PartId = null,
    string? Text = null,
    string? ToolName = null,
    ToolState? ToolState = null,
    string? ToolOutput = null,
    IReadOnlyDictionary<string, JsonElement>? ToolInput = null,
    RemoteQuestion? Question = null
);

public sealed record RemoteQuestion(
    string RequestId,
    string Title,
    IReadOnlyList<RemoteQuestionItem> Questions);

public sealed record RemoteQuestionItem(
    string Id,
    string Header,
    string Question,
    bool Multiple,
    bool Custom,
    IReadOnlyList<RemoteQuestionOption> Options);

public sealed record RemoteQuestionOption(
    string Label,
    string? Description,
    string Value);
