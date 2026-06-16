using System;
using System.Collections.Generic;
using AgentOrchestrator.App.Models.Chat;
using AgentOrchestrator.App.Models.Sidebar;

namespace AgentOrchestrator.App.Services.Agent;

/// <summary>
/// One streaming chunk from a chat session. The <see cref="Kind"/>
/// describes what kind of block the chunk contributes to; multiple
/// chunks for the same <see cref="MessageId"/> concatenate.
/// </summary>
public sealed record ChatStreamChunk(
    string MessageId,
    ChatBlockKind Kind,
    string Content,
    bool Complete
);
