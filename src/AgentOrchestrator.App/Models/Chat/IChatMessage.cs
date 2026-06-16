using System.Collections.Generic;

namespace AgentOrchestrator.App.Models.Chat;

public interface IChatMessage
{
    string Id { get; }
    ChatRole Role { get; }
    string Author { get; }
    bool IsStreaming { get; }
    IReadOnlyList<IChatBlock> Blocks { get; }
}
