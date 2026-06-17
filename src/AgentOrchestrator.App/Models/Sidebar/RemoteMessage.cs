using System.Collections.Generic;
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
    string? ToolOutput = null
);
