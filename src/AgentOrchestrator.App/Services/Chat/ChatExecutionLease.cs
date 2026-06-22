using System;

namespace AgentOrchestrator.App.Services.Chat;

public sealed class ChatExecutionLease
{
    public string ConversationSessionId { get; }
    public string GraphId { get; }
    public DateTimeOffset AcquiredAt { get; }

    public ChatExecutionLease(string conversationSessionId, string graphId)
    {
        ConversationSessionId = conversationSessionId;
        GraphId = graphId;
        AcquiredAt = DateTimeOffset.UtcNow;
    }
}
