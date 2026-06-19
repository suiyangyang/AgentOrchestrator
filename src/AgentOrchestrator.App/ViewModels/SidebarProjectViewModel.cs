using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.CompilerServices;
using AgentOrchestrator.App.Models.Sidebar;

namespace AgentOrchestrator.App.ViewModels;

/// <summary>
/// A project group node in the sidebar tree. Owns its child sessions and
/// tracks its own expand/collapse / current / pinned state.
/// </summary>
public sealed class SidebarProjectViewModel : INotifyPropertyChanged
{
    private const int SessionPageSize = 5;

    public SidebarProjectViewModel(ProjectRecord record)
    {
        _record = record;
        Sessions.CollectionChanged += OnSessionsCollectionChanged;
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

    public ObservableCollection<SidebarSessionViewModel> VisibleSessions { get; } = [];

    public bool HasMoreSessions => Sessions.Count > VisibleSessionCount;

    public bool HasSessionPaginationControls => Sessions.Count > SessionPageSize;

    public bool CanCollapseSessions => VisibleSessionCount > SessionPageSize && Sessions.Count > SessionPageSize;

    public int VisibleSessionCount
    {
        get => _visibleSessionCount;
        private set
        {
            var clamped = value < SessionPageSize ? SessionPageSize : value;
            if (_visibleSessionCount == clamped) return;
            _visibleSessionCount = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasMoreSessions));
            OnPropertyChanged(nameof(HasSessionPaginationControls));
            OnPropertyChanged(nameof(CanCollapseSessions));
            RefreshVisibleSessions();
        }
    }

    private int _visibleSessionCount = SessionPageSize;

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

    public void ShowMoreSessions()
    {
        if (!HasMoreSessions) return;
        VisibleSessionCount += SessionPageSize;
    }

    public void CollapseSessions()
    {
        VisibleSessionCount = SessionPageSize;
    }

    private void OnSessionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasSessions));
        OnPropertyChanged(nameof(HasMoreSessions));
        OnPropertyChanged(nameof(HasSessionPaginationControls));
        OnPropertyChanged(nameof(CanCollapseSessions));
        RefreshVisibleSessions();
    }

    private void RefreshVisibleSessions()
    {
        var limit = Math.Min(VisibleSessionCount, Sessions.Count);
        VisibleSessions.Clear();
        foreach (var session in Sessions.Take(limit))
        {
            VisibleSessions.Add(session);
        }
        OnPropertyChanged(nameof(HasMoreSessions));
        OnPropertyChanged(nameof(HasSessionPaginationControls));
        OnPropertyChanged(nameof(CanCollapseSessions));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
