using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgentOrchestrator.App.Models.Sidebar;

namespace AgentOrchestrator.App.ViewModels;

/// <summary>
/// A project group node in the sidebar tree. Owns its child sessions and
/// tracks its own expand/collapse state.
/// </summary>
public sealed class SidebarProjectViewModel : INotifyPropertyChanged
{
    public SidebarProjectViewModel(ProjectRecord record)
    {
        Record = record;
    }

    public ProjectRecord Record { get; }
    public string ProjectId => Record.Id;
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

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
