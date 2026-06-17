using AgentOrchestrator.App.Models.Chat;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentOrchestrator.App.ViewModels;

public partial class ChatBlockViewModel : ObservableObject, IChatBlock
{
    public ChatBlockViewModel(ChatBlockKind kind, string? partId, string? text = null, string? assetPath = null, bool isExpanded = false)
    {
        Kind = kind;
        PartId = partId;
        _text = text;
        _assetPath = assetPath;
        _isExpanded = isExpanded;
    }

    public ChatBlockViewModel(ChatBlockKind kind, string? partId, string title, ToolState toolState, string? toolOutput, bool isExpanded = false)
    {
        Kind = kind;
        PartId = partId;
        _toolName = title;
        _toolState = toolState;
        _toolOutput = toolOutput;
        _isExpanded = isExpanded;
    }

    public ChatBlockKind Kind { get; }

    public string? PartId { get; }

    [ObservableProperty]
    private string? _text;

    [ObservableProperty]
    private string? _assetPath;

    public bool IsText => Kind == ChatBlockKind.Text;

    public bool IsThought => Kind == ChatBlockKind.Thought;

    public bool IsImage => Kind == ChatBlockKind.Image;

    public bool IsTool => Kind == ChatBlockKind.Tool;

    public bool IsTask => Kind == ChatBlockKind.Task;

    public bool ShowStateText => IsTool || IsTask;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private string? _toolName;

    [ObservableProperty]
    private ToolState _toolState;

    [ObservableProperty]
    private string? _toolOutput;

    partial void OnToolStateChanged(ToolState value)
    {
        OnPropertyChanged(nameof(ToolStateText));
    }

    partial void OnToolNameChanged(string? value)
    {
        OnPropertyChanged(nameof(HeaderText));
    }

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
        ChatBlockKind.Task => ToolName ?? "Task",
        ChatBlockKind.Thought => "Thinking",
        _ => string.Empty
    };

    public string IconKind => Kind == ChatBlockKind.Thought
        ? "thought"
        : ((Kind == ChatBlockKind.Tool || Kind == ChatBlockKind.Task) ? "tool" : string.Empty);

    public bool ShowThoughtIcon => Kind == ChatBlockKind.Thought;
    public bool ShowToolIcon => Kind == ChatBlockKind.Tool || Kind == ChatBlockKind.Task;
}
