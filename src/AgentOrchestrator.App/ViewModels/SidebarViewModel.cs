using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.Sidebar;
using AgentOrchestrator.App.Services.Sidebar;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentOrchestrator.App.ViewModels;

/// <summary>
/// Owns the sidebar tree. Two top-level groups: "项目" (project nodes with
/// nested sessions) and "对话" (orphan sessions with no project).
///
/// Search filters by Title (substring, case-insensitive) and only shows
/// matching sessions plus the projects that contain them; groups that end
/// up empty are hidden.
/// </summary>
public sealed partial class SidebarViewModel : ViewModelBase
{
    private readonly ISidebarRepository _repo;
    private readonly Dictionary<string, SidebarProjectViewModel> _projectsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SidebarSessionViewModel> _sessionsById = new(StringComparer.Ordinal);
    private readonly ObservableCollection<SidebarSessionViewModel> _orphanSessions = [];
    private CancellationTokenSource? _searchDebounce;

    public SidebarViewModel(ISidebarRepository repo)
    {
        _repo = repo;
    }

    /// <summary>All project groups (top-level "项目" section).</summary>
    public ObservableCollection<SidebarProjectViewModel> Projects { get; } = [];

    /// <summary>Sessions with no project (top-level "对话" section).</summary>
    public ObservableCollection<SidebarSessionViewModel> OrphanSessions => _orphanSessions;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isProjectsVisible = true;

    [ObservableProperty]
    private bool _isOrphansVisible = true;

    /// <summary>Raised when the user clicks a session in the tree.</summary>
    public event EventHandler<string>? SessionSelected;

    /// <summary>Raised when the user clicks the "新对话" button.</summary>
    public event EventHandler? NewSessionRequested;

    /// <summary>Raised when the user clicks the "任务编排" button.</summary>
    public event EventHandler? TaskGraphRequested;

    /// <summary>Raised when the user clicks "新建工作目录" inside a project group.</summary>
    public event EventHandler? AddProjectRequested;

    /// <summary>Load the tree from the local repository. Safe to call multiple times.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var projects = await _repo.ListProjectsAsync(ct).ConfigureAwait(true);
        var sessions = await _repo.ListSessionsAsync(ct).ConfigureAwait(true);

        Projects.Clear();
        _projectsById.Clear();
        foreach (var p in projects.OrderByDescending(p => p.CreatedAt))
        {
            var pvm = new SidebarProjectViewModel(p);
            Projects.Add(pvm);
            _projectsById[p.Id] = pvm;
        }

        _orphanSessions.Clear();
        _sessionsById.Clear();
        foreach (var s in sessions.OrderByDescending(s => s.CreatedAt))
        {
            AddSessionToTree(new SidebarSessionViewModel(s));
        }

        ApplyFilter();
    }

    /// <summary>Add or update a session in the tree. Idempotent by SessionId.</summary>
    public void AddOrUpdateSession(SessionRecord record)
    {
        if (_sessionsById.TryGetValue(record.SessionId, out var existing))
        {
            existing.UpdateRecord(record);
            return;
        }

        AddSessionToTree(new SidebarSessionViewModel(record));
        ApplyFilter();
    }

    /// <summary>Update a session's Title in place (no rebind needed).</summary>
    public void UpdateSessionTitle(string sessionId, string newTitle)
    {
        if (_sessionsById.TryGetValue(sessionId, out var vm))
        {
            vm.Title = newTitle;
        }
    }

    private void AddSessionToTree(SidebarSessionViewModel vm)
    {
        _sessionsById[vm.SessionId] = vm;
        if (vm.ProjectId is not null && _projectsById.TryGetValue(vm.ProjectId, out var project))
        {
            project.Sessions.Add(vm);
            var sorted = project.Sessions.OrderByDescending(s => s.Record.CreatedAt).ToList();
            project.Sessions.Clear();
            foreach (var s in sorted) project.Sessions.Add(s);
        }
        else
        {
            _orphanSessions.Add(vm);
        }
    }

    [RelayCommand]
    private void SelectSession(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        ClearSelection();
        if (_sessionsById.TryGetValue(sessionId, out var vm))
        {
            vm.IsSelected = true;
        }
        SessionSelected?.Invoke(this, sessionId);
    }

    [RelayCommand]
    private void RequestNewSession() => NewSessionRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void RequestTaskGraph() => TaskGraphRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void RequestAddProject() => AddProjectRequested?.Invoke(this, EventArgs.Empty);

    private void ClearSelection()
    {
        foreach (var p in Projects)
        {
            foreach (var s in p.Sessions) s.IsSelected = false;
        }
        foreach (var s in _orphanSessions) s.IsSelected = false;
    }

    partial void OnSearchTextChanged(string value)
    {
        // Debounce 200ms before applying filter, per spec §6.6.
        _searchDebounce?.Cancel();
        _searchDebounce = new CancellationTokenSource();
        var token = _searchDebounce.Token;
        _ = Task.Delay(200, token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(ApplyFilter);
        }, TaskScheduler.Default);
    }

    private void ApplyFilter()
    {
        var query = (SearchText ?? string.Empty).Trim();
        if (query.Length == 0)
        {
            // No filter — show everything.
            IsProjectsVisible = Projects.Count > 0;
            IsOrphansVisible = _orphanSessions.Count > 0;
            foreach (var p in Projects)
            {
                p.IsExpanded = true;
                foreach (var s in p.Sessions) s.IsSelected = false; // clear filter match
            }
            return;
        }

        // Filter: keep sessions whose Title contains query (case-insensitive).
        var hitSessionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in Projects)
        {
            var anyHit = false;
            foreach (var s in p.Sessions)
            {
                if (s.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    hitSessionIds.Add(s.SessionId);
                    anyHit = true;
                }
            }
            p.IsExpanded = anyHit;
        }

        var anyOrphanHit = false;
        foreach (var s in _orphanSessions)
        {
            if (s.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                hitSessionIds.Add(s.SessionId);
                anyOrphanHit = true;
            }
        }

        IsProjectsVisible = Projects.Any(p => p.Sessions.Any(s => hitSessionIds.Contains(s.SessionId)));
        IsOrphansVisible = anyOrphanHit;
    }
}
