using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.Services.TaskGraph;

namespace AgentOrchestrator.App.ViewModels;

public sealed class SidebarTaskGraphItemViewModel : INotifyPropertyChanged
{
    public SidebarTaskGraphItemViewModel(TaskGraphListItem item)
    {
        _item = item;
        _name = string.IsNullOrWhiteSpace(item.Name) ? "未命名编排" : item.Name;
    }

    private TaskGraphListItem _item;
    public TaskGraphListItem Item
    {
        get => _item;
        set
        {
            if (_item == value)
            {
                return;
            }

            _item = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Id));
            OnPropertyChanged(nameof(UpdatedAt));
            OnPropertyChanged(nameof(DisplayRelativeTime));
            OnPropertyChanged(nameof(ExecutionState));
            OnPropertyChanged(nameof(ExecutionStateText));
            OnPropertyChanged(nameof(NodeCount));
            OnPropertyChanged(nameof(TemplateKind));
            OnPropertyChanged(nameof(TemplateText));
            OnPropertyChanged(nameof(UpdatedAtText));
            OnPropertyChanged(nameof(SubtitleText));
        }
    }

    public string Id => Item.Id;

    private string _name;
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
            {
                return;
            }

            _name = value;
            OnPropertyChanged();
        }
    }

    public DateTimeOffset UpdatedAt => Item.UpdatedAt;

    public TaskGraphExecutionState ExecutionState => Item.ExecutionState;

    public int NodeCount => Item.NodeCount;

    public TaskGraphTemplateKind TemplateKind => Item.TemplateKind;

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

    public string UpdatedAtText => UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string SubtitleText => $"{TemplateText} · {NodeCount} 个节点 · {ExecutionStateText}";

    public string DisplayRelativeTime => SidebarSessionViewModel.FormatRelative(UpdatedAt.ToUnixTimeMilliseconds());

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public void Update(TaskGraphListItem item)
    {
        Item = item;
        Name = string.IsNullOrWhiteSpace(item.Name) ? "未命名编排" : item.Name;
        OnPropertyChanged(nameof(DisplayRelativeTime));
        OnPropertyChanged(nameof(UpdatedAtText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
