using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentOrchestrator.App.ViewModels;

public partial class TodoItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private string _priority = string.Empty;

    public bool IsInProgress => StatusEquals("in_progress");
    public bool IsCompleted => StatusEquals("completed");
    public bool IsCancelled => StatusEquals("cancelled");
    public bool IsPending => !IsInProgress && !IsCompleted && !IsCancelled;

    public string StatusText => Status switch
    {
        "in_progress" => "进行中",
        "completed" => "已完成",
        "cancelled" => "已取消",
        "pending" => "待处理",
        _ => string.IsNullOrWhiteSpace(Status) ? "待处理" : Status,
    };

    public string PriorityText => Priority switch
    {
        "high" => "高优先级",
        "medium" => "中优先级",
        "low" => "低优先级",
        _ => string.IsNullOrWhiteSpace(Priority) ? "普通" : Priority,
    };

    partial void OnStatusChanged(string value)
    {
        OnPropertyChanged(nameof(IsInProgress));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(IsCancelled));
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnPriorityChanged(string value)
        => OnPropertyChanged(nameof(PriorityText));

    private bool StatusEquals(string expected)
        => string.Equals(Status, expected, System.StringComparison.OrdinalIgnoreCase);
}
