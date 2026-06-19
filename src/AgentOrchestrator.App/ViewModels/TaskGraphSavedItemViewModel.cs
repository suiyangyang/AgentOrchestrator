using System;
using AgentOrchestrator.App.Models.TaskGraph;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentOrchestrator.App.ViewModels;

public sealed partial class TaskGraphSavedItemViewModel : ObservableObject
{
    public TaskGraphSavedItemViewModel(string id, string name, DateTimeOffset updatedAt, TaskGraphExecutionState executionState, int nodeCount)
    {
        Id = id;
        Name = name;
        UpdatedAt = updatedAt;
        ExecutionState = executionState;
        NodeCount = nodeCount;
        TemplateKind = TaskGraphTemplateKind.Custom;
    }

    public TaskGraphSavedItemViewModel(string id, string name, DateTimeOffset updatedAt, TaskGraphExecutionState executionState, int nodeCount, TaskGraphTemplateKind templateKind)
    {
        Id = id;
        Name = name;
        UpdatedAt = updatedAt;
        ExecutionState = executionState;
        NodeCount = nodeCount;
        TemplateKind = templateKind;
    }

    public string Id { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private DateTimeOffset _updatedAt;

    [ObservableProperty]
    private TaskGraphExecutionState _executionState;

    [ObservableProperty]
    private int _nodeCount;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private TaskGraphTemplateKind _templateKind;

    public string UpdatedAtText => UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string ExecutionStateText => ExecutionState switch
    {
        TaskGraphExecutionState.Draft => "草稿",
        TaskGraphExecutionState.Running => "执行中",
        TaskGraphExecutionState.WaitingForInput => "等待确认",
        TaskGraphExecutionState.Completed => "已完成",
        TaskGraphExecutionState.Failed => "失败",
        TaskGraphExecutionState.Cancelled => "已取消",
        _ => ExecutionState.ToString(),
    };

    public string TemplateText => TemplateKind switch
    {
        TaskGraphTemplateKind.TaskList => "任务列表",
        TaskGraphTemplateKind.FeatureDevelopment => "功能开发",
        TaskGraphTemplateKind.BugList => "Bug 列表",
        _ => "自定义",
    };

    partial void OnUpdatedAtChanged(DateTimeOffset value)
        => OnPropertyChanged(nameof(UpdatedAtText));

    partial void OnExecutionStateChanged(TaskGraphExecutionState value)
        => OnPropertyChanged(nameof(ExecutionStateText));

    partial void OnTemplateKindChanged(TaskGraphTemplateKind value)
        => OnPropertyChanged(nameof(TemplateText));
}
