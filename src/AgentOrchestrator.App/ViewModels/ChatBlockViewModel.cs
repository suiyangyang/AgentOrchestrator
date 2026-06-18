using System.Collections.Generic;
using System.Text.Json;
using AgentOrchestrator.App.Models.Chat;
using AgentOrchestrator.App.Models.Sidebar;
using AgentOrchestrator.App.Services;
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

    public bool IsRunning => (IsTool || IsTask) && ToolState == ToolState.Running;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private string? _toolName;

    [ObservableProperty]
    private ToolState _toolState;

    [ObservableProperty]
    private string? _toolOutput;

    [ObservableProperty]
    private IReadOnlyDictionary<string, JsonElement>? _toolInput;

    [ObservableProperty]
    private string? _toolWorkingDirectory;

    [ObservableProperty]
    private RemoteQuestion? _toolQuestion;

    partial void OnToolStateChanged(ToolState value)
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(ShowHeaderText));
        OnPropertyChanged(nameof(ShowThoughtLeadingIcon));
        OnPropertyChanged(nameof(ShowToolLeadingIcon));
    }

    partial void OnToolNameChanged(string? value)
    {
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(ShowHeaderText));
        NotifyToolHeaderPropertiesChanged();
    }

    partial void OnToolOutputChanged(string? value)
        => NotifyToolHeaderPropertiesChanged();

    partial void OnToolWorkingDirectoryChanged(string? value)
        => NotifyToolHeaderPropertiesChanged();

    partial void OnToolInputChanged(IReadOnlyDictionary<string, JsonElement>? value)
        => NotifyToolHeaderPropertiesChanged();

    public ToolDisplayInfo ToolDisplay => ToolDisplayParser.Parse(ToolName, ToolName, ToolOutput, ToolWorkingDirectory, input: ToolInput);

    public string ToolHeaderActionText => ToolDisplay.PrimaryText;

    public string? ToolHeaderFilePathText => ToolDisplay.FilePath;

    public string? ToolHeaderLineRangeText => ToolDisplay.LineRangeText;

    public string? ToolHeaderLineRangeDisplayText => HasToolHeaderFilePath
        ? $", {ToolHeaderLineRangeText}"
        : ToolHeaderLineRangeText;

    public bool HasToolHeaderFilePath => !string.IsNullOrWhiteSpace(ToolHeaderFilePathText);

    public bool HasToolHeaderLineRange => !string.IsNullOrWhiteSpace(ToolHeaderLineRangeText);

    public bool ShowToolHeader => (IsTool || IsTask) && ShowHeaderText;

    public bool ShowPlainHeader => !(IsTool || IsTask) && ShowHeaderText;

    public string HeaderText => Kind switch
    {
        ChatBlockKind.Tool => ToolDisplay.DisplayTitle,
        ChatBlockKind.Task => ToolDisplay.DisplayTitle,
        ChatBlockKind.Thought => "Thinking",
        _ => string.Empty
    };

    public bool ShowHeaderText => !(IsThought && IsRunning) && !string.IsNullOrWhiteSpace(HeaderText);

    public bool ShowThoughtLeadingIcon => ShowThoughtIcon && !IsRunning;

    public bool ShowToolLeadingIcon => ShowToolIcon && !IsRunning;

    public string IconKind => Kind == ChatBlockKind.Thought
        ? "thought"
        : ((Kind == ChatBlockKind.Tool || Kind == ChatBlockKind.Task) ? "tool" : string.Empty);

    public bool ShowThoughtIcon => Kind == ChatBlockKind.Thought;
    public bool ShowToolIcon => Kind == ChatBlockKind.Tool || Kind == ChatBlockKind.Task;

    private void NotifyToolHeaderPropertiesChanged()
    {
        OnPropertyChanged(nameof(ToolDisplay));
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(ToolHeaderActionText));
        OnPropertyChanged(nameof(ToolHeaderFilePathText));
        OnPropertyChanged(nameof(ToolHeaderLineRangeText));
        OnPropertyChanged(nameof(ToolHeaderLineRangeDisplayText));
        OnPropertyChanged(nameof(HasToolHeaderFilePath));
        OnPropertyChanged(nameof(HasToolHeaderLineRange));
        OnPropertyChanged(nameof(ShowToolHeader));
    }
}
