using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgentOrchestrator.App.Models.Sidebar;

namespace AgentOrchestrator.App.ViewModels;

/// <summary>
/// A project group node in the sidebar tree. Owns its child sessions and
/// tracks its own expand/collapse / current / pinned state.
/// </summary>
public sealed class SidebarProjectViewModel : INotifyPropertyChanged
{
    public SidebarProjectViewModel(ProjectRecord record)
    {
        _record = record;
    }

    private ProjectRecord _record;
    public ProjectRecord Record
    {
        get => _record;
        set
        {
            if (_record == value) return;
            _record = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Id));
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Directory));
        }
    }

    public string Id => Record.Id;
    public string Name => Record.Name;
    public string Directory => Record.Directory;

    public ObservableCollection<SidebarSessionViewModel> Sessions { get; } = [];

    public bool HasSessions => Sessions.Count > 0;

    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    private bool _isCurrent;
    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent == value) return;
            _isCurrent = value;
            OnPropertyChanged();
        }
    }

    private bool _isPinned;
    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (_isPinned == value) return;
            _isPinned = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
