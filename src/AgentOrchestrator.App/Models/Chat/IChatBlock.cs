namespace AgentOrchestrator.App.Models.Chat;

public enum ToolState
{
    Pending,
    Running,
    Completed,
    Failed
}

public interface IChatBlock
{
    ChatBlockKind Kind { get; }
    string? Text { get; }
    string? AssetPath { get; }
    bool IsExpanded { get; set; }
    string? ToolName { get; }
    ToolState ToolState { get; }
    string? ToolOutput { get; }
}
