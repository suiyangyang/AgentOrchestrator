using AgentOrchestrator.App.Models.Chat;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentOrchestrator.App.ViewModels;

public partial class ChatBlockViewModel : ObservableObject, IChatBlock
{
    public ChatBlockViewModel(ChatBlockKind kind, string? text = null, string? assetPath = null, bool isExpanded = false)
    {
        Kind = kind;
        Text = text;
        AssetPath = assetPath;
        _isExpanded = isExpanded;
    }

    public ChatBlockViewModel(string toolName, ToolState toolState, string? toolOutput, bool isExpanded = false)
    {
        Kind = ChatBlockKind.Tool;
        ToolName = toolName;
        _toolState = toolState;
        ToolOutput = toolOutput;
        _isExpanded = isExpanded;
    }

    public ChatBlockKind Kind { get; }

    public string? Text { get; }

    public string? AssetPath { get; }

    public bool IsText => Kind == ChatBlockKind.Text;

    public bool IsThought => Kind == ChatBlockKind.Thought;

    public bool IsImage => Kind == ChatBlockKind.Image;

    public bool IsTool => Kind == ChatBlockKind.Tool;

    [ObservableProperty]
    private bool _isExpanded;

    public string? ToolName { get; }

    [ObservableProperty]
    private ToolState _toolState;

    public string? ToolOutput { get; }

    public string ToolStateText => ToolState switch
    {
        ToolState.Running => "Running…",
        ToolState.Completed => "Completed",
        ToolState.Failed => "Failed",
        _ => "Pending"
    };

    public string HeaderText => Kind switch
    {
        ChatBlockKind.Tool => ToolName ?? string.Empty,
        ChatBlockKind.Thought => "Thinking",
        _ => string.Empty
    };

    public string IconKind => Kind == ChatBlockKind.Thought
        ? "thought"
        : (Kind == ChatBlockKind.Tool ? "tool" : string.Empty);

    public bool ShowThoughtIcon => Kind == ChatBlockKind.Thought;
    public bool ShowToolIcon => Kind == ChatBlockKind.Tool;
}
