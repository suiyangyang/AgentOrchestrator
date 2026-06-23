using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.Services.TaskGraph;

namespace AgentOrchestrator.App.ViewModels;

/// <summary>
/// Row item view model for a template in the left navigation panel.
/// Uses manual INotifyPropertyChanged (mirroring SidebarTaskGraphItemViewModel pattern).
/// </summary>
public sealed class TaskTemplateItemViewModel : INotifyPropertyChanged
{
    public TaskTemplateItemViewModel(TaskTemplateListItem item)
    {
        _item = item;
        _name = string.IsNullOrWhiteSpace(item.Name) ? "未命名模板" : item.Name;
    }

    private TaskTemplateListItem _item;
    public TaskTemplateListItem Item
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
            OnPropertyChanged(nameof(BaseKind));
            OnPropertyChanged(nameof(BaseKindText));
            OnPropertyChanged(nameof(IsBuiltIn));
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

    public TaskGraphTemplateKind BaseKind => Item.BaseKind;

    public string BaseKindText => Item.BaseKind switch
    {
        TaskGraphTemplateKind.TaskList => "任务列表",
        TaskGraphTemplateKind.FeatureDevelopment => "功能开发",
        TaskGraphTemplateKind.BugList => "Bug 列表",
        _ => "自定义",
    };

    public bool IsBuiltIn => Item.IsBuiltIn;

    public string DisplayRelativeTime =>
        SidebarSessionViewModel.FormatRelative(UpdatedAt.ToUnixTimeMilliseconds());

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

    /// <summary>
    /// When true, the row renders a TextBox for inline rename instead of a TextBlock.
    /// </summary>
    private bool _isEditing;
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (_isEditing == value)
            {
                return;
            }

            _isEditing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNotEditing));
        }
    }

    public bool IsNotEditing => !IsEditing;

    public void Update(TaskTemplateListItem item)
    {
        Item = item;
        Name = string.IsNullOrWhiteSpace(item.Name) ? "未命名模板" : item.Name;
        OnPropertyChanged(nameof(DisplayRelativeTime));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
