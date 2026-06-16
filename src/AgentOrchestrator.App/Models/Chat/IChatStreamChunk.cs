namespace AgentOrchestrator.App.Models.Chat;

public interface IChatStreamChunk
{
    string MessageId { get; }
    ChatBlockKind Kind { get; }
    string Content { get; }
    bool Complete { get; }
}
