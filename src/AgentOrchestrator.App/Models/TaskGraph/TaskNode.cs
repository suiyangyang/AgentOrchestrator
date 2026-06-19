using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentOrchestrator.App.Models.TaskGraph;

public sealed partial class TaskNode : ObservableObject
{
    private bool _isSelected;

    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _title = "新任务";

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private TaskNodeKind _kind = TaskNodeKind.Execute;

    [ObservableProperty]
    private TaskNodeStatus _status = TaskNodeStatus.Pending;

    [ObservableProperty]
    private string? _agentHint;

    [ObservableProperty]
    private NodePosition _position = new(40, 40);

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    private string? _agentSessionId;

    [ObservableProperty]
    private int _attemptCount;

    [ObservableProperty]
    private string? _lastError;

    [ObservableProperty]
    private string? _outputSummary;

    [ObservableProperty]
    private string? _rawOutput;

    [ObservableProperty]
    private DateTimeOffset? _startedAt;

    [ObservableProperty]
    private DateTimeOffset? _completedAt;

    public ObservableCollection<string> Tags { get; set; } = [];

    public ObservableCollection<string> DependsOn { get; set; } = [];

    public ObservableCollection<string> TouchedFiles { get; set; } = [];

    public ObservableCollection<string> ResultTags { get; set; } = [];

    [JsonIgnore]
    public bool CanRetry => Status == TaskNodeStatus.Failed && AttemptCount < 3;

    [JsonIgnore]
    public bool CanOpenDetail => !string.IsNullOrWhiteSpace(AgentSessionId);

    [JsonIgnore]
    public bool IsExecutable => Kind != TaskNodeKind.HumanInput;

    [JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(NodeBorderBrush));
                OnPropertyChanged(nameof(NodeBackground));
            }
        }
    }

    /// <summary>True when the user dragged on empty canvas to spawn this
    /// node — it lives in the graph but is not yet "real" (no prompt, no
    /// agent). Clicking the node promotes it to a real, editable node.</summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isPending;

    [JsonIgnore]
    public string NodeBorderBrush => IsSelected ? "#2459B8" : "#E2E5EA";

    [JsonIgnore]
    public string NodeBackground => IsSelected ? "#F5F9FF" : "#FFFFFF";

    [JsonIgnore]
    public string StatusText => Status switch
    {
        TaskNodeStatus.Pending => "待执行",
        TaskNodeStatus.Running => "执行中",
        TaskNodeStatus.Completed => "已完成",
        TaskNodeStatus.Failed => "失败",
        TaskNodeStatus.Skipped => "已跳过",
        _ => Status.ToString(),
    };

    partial void OnStatusChanged(TaskNodeStatus value)
    {
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(NodeBorderBrush));
    }

    partial void OnAttemptCountChanged(int value)
    {
        OnPropertyChanged(nameof(CanRetry));
    }

    partial void OnAgentSessionIdChanged(string? value)
    {
        OnPropertyChanged(nameof(CanOpenDetail));
    }

    partial void OnIsPendingChanged(bool value)
    {
        OnPropertyChanged(nameof(NodeBorderBrush));
        OnPropertyChanged(nameof(NodeBackground));
    }
}
