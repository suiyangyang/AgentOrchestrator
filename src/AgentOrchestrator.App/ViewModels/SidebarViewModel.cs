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
using AgentOrchestrator.App.Services.TaskGraph;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentOrchestrator.App.ViewModels;

/// <summary>
/// Owns the sidebar tree. Two top-level groups: "项目" (project nodes with
/// nested sessions) and "对话" (orphan sessions with no project).
///
/// Search filters by Title (substring, case-insensitive) and only shows
/// matching sessions plus the projects that contain them.
///
/// Section headers are always rendered, even when their group is empty,
/// so the user can always add a project or a new chat from the sidebar.
/// </summary>
public sealed partial class SidebarViewModel : ViewModelBase
{
    private readonly ISidebarRepository _repo;
    private readonly ITaskGraphStore _taskGraphStore;
    private readonly Dictionary<string, SidebarProjectViewModel> _projectsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SidebarSessionViewModel> _sessionsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SidebarTaskGraphItemViewModel> _taskGraphsById = new(StringComparer.Ordinal);
    private readonly ObservableCollection<SidebarSessionViewModel> _orphanSessions = [];

    public SidebarViewModel(ISidebarRepository repo, ITaskGraphStore taskGraphStore)
    {
        _repo = repo;
        _taskGraphStore = taskGraphStore;
        Projects.CollectionChanged += (s, e) =>
        {
            if (e.Action is System.Collections.Specialized.NotifyCollectionChangedAction
                    .Add or System.Collections.Specialized.NotifyCollectionChangedAction.Remove
                    or System.Collections.Specialized.NotifyCollectionChangedAction.Reset
                    or System.Collections.Specialized.NotifyCollectionChangedAction.Replace)
            {
                OnPropertyChanged(nameof(HasProjects));
            }
        };
    }

    public ObservableCollection<SidebarProjectViewModel> Projects { get; } = [];
    public ObservableCollection<SidebarSessionViewModel> OrphanSessions => _orphanSessions;
    public ObservableCollection<SidebarTaskGraphItemViewModel> TaskGraphs { get; } = [];
    public ObservableCollection<SidebarSessionViewModel> SearchResults { get; } = [];

    public bool HasProjects => Projects.Count > 0;
    public bool HasTaskGraphs => TaskGraphs.Count > 0;
    public bool HasSearchResults => SearchResults.Count > 0;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isSearchOverlayVisible;

    /// <summary>Currently focused project (or null = no project selected / orphan chat).</summary>
    [ObservableProperty]
    private SidebarProjectViewModel? _currentProject;

    [ObservableProperty]
    private bool _areProjectsExpanded = true;

    [ObservableProperty]
    private bool _isOrphansExpanded = true;

    [ObservableProperty]
    private bool _isTaskGraphsExpanded = true;

    /// <summary>Working directory used when creating a new session on the blank page.</summary>
    public string? CurrentWorkingDirectory => CurrentProject?.Directory;

    /// <summary>Raised when the user clicks a session in the tree.</summary>
    public event EventHandler<string>? SessionSelected;

    /// <summary>Raised when the user clicks the "新对话" button (or "+" on a project).</summary>
    public event EventHandler<NewSessionContext>? NewSessionRequested;

    /// <summary>Raised when the user clicks the "任务编排" button.</summary>
    public event EventHandler? TaskGraphRequested;

    /// <summary>Raised when the user clicks a saved task graph in the tree.</summary>
    public event EventHandler<string>? TaskGraphSelected;

    /// <summary>Raised when the user requests a new task graph.</summary>
    public event EventHandler? NewTaskGraphRequested;

    /// <summary>Raised when the user clicks a task graph item to open it in the workspace.</summary>
    public event EventHandler<string>? TaskGraphOpenRequested;

    /// <summary>Raised when the user opens the "..." menu on a task graph item.</summary>
    public event EventHandler<TaskGraphActionRequest>? TaskGraphActionRequested;

    /// <summary>Raised when the user clicks the "+" on the 项目 section header.</summary>
    public event EventHandler? AddProjectRequested;

    /// <summary>Raised when the user opens the "..." menu on a project.</summary>
    public event EventHandler<ProjectActionRequest>? ProjectActionRequested;

    /// <summary>Raised when the user opens the "..." menu on a session.</summary>
    public event EventHandler<SessionActionRequest>? SessionActionRequested;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var projects = await _repo.ListProjectsAsync(ct).ConfigureAwait(true);
        var sessions = await _repo.ListSessionsAsync(ct).ConfigureAwait(true);
        var taskGraphs = await _taskGraphStore.ListAsync(ct).ConfigureAwait(true);

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

        TaskGraphs.Clear();
        _taskGraphsById.Clear();
        foreach (var graph in taskGraphs.OrderByDescending(x => x.UpdatedAt))
        {
            var graphVm = new SidebarTaskGraphItemViewModel(graph);
            TaskGraphs.Add(graphVm);
            _taskGraphsById[graph.Id] = graphVm;
        }

        SyncProjectsExpandedState();
        RefreshSearchResults();
        OnPropertyChanged(nameof(HasTaskGraphs));
    }

    public void AddOrUpdateSession(SessionRecord record)
    {
        if (_sessionsById.TryGetValue(record.SessionId, out var existing))
        {
            existing.UpdateRecord(record);
            return;
        }
        AddSessionToTree(new SidebarSessionViewModel(record));
        RefreshSearchResults();
    }

    public void SetSessionStreaming(string sessionId, bool isStreaming)
    {
        if (_sessionsById.TryGetValue(sessionId, out var vm))
        {
            vm.IsStreaming = isStreaming;
        }
    }

    public void MarkSessionViewed(string sessionId, long viewedAt)
    {
        if (_sessionsById.TryGetValue(sessionId, out var vm))
        {
            vm.UpdateRecord(vm.Record with { ViewedAt = viewedAt });
            RefreshSearchResults();
        }
    }

    public void UpdateSessionTitle(string sessionId, string newTitle)
    {
        if (_sessionsById.TryGetValue(sessionId, out var vm))
        {
            vm.Title = newTitle;
            RefreshSearchResults();
        }
    }

    /// <summary>Adds a project (used by the folder-picker flow) and selects it.</summary>
    public async Task<ProjectRecord> AddProjectAsync(string name, string directory, CancellationToken ct = default)
    {
        var record = await _repo.CreateProjectAsync(name, directory, ct).ConfigureAwait(true);
        var pvm = new SidebarProjectViewModel(record);
        Projects.Insert(0, pvm);
        _projectsById[record.Id] = pvm;
        SetCurrentProject(pvm);
        return record;
    }

    /// <summary>Updates a project's name in place.</summary>
    public async Task RenameProjectAsync(string projectId, string newName, CancellationToken ct = default)
    {
        await _repo.RenameProjectAsync(projectId, newName, ct).ConfigureAwait(true);
        if (_projectsById.TryGetValue(projectId, out var pvm))
        {
            pvm.Record = pvm.Record with { Name = newName };
            pvm.OnPropertyChanged(nameof(SidebarProjectViewModel.Name));
        }
    }

    /// <summary>Removes a project and all of its sessions from the local tree and SQLite.</summary>
    public async Task RemoveProjectAsync(string projectId, CancellationToken ct = default)
    {
        if (_projectsById.TryGetValue(projectId, out var pvm))
        {
            foreach (var s in pvm.Sessions.ToList())
            {
                _sessionsById.Remove(s.SessionId);
            }
            Projects.Remove(pvm);
            _projectsById.Remove(projectId);
            if (CurrentProject == pvm) CurrentProject = null;
        }
        await _repo.DeleteProjectAsync(projectId, ct).ConfigureAwait(true);
    }

    public async Task RemoveSessionAsync(string sessionId, CancellationToken ct = default)
    {
        if (_sessionsById.TryGetValue(sessionId, out var vm))
        {
            _sessionsById.Remove(sessionId);
            if (vm.ProjectId is not null && _projectsById.TryGetValue(vm.ProjectId, out var project))
            {
                project.Sessions.Remove(vm);
            }
            else
            {
                _orphanSessions.Remove(vm);
            }
        }
        await _repo.DeleteSessionAsync(sessionId, ct).ConfigureAwait(true);
        RefreshSearchResults();
    }

    public async Task RefreshTaskGraphsAsync(CancellationToken ct = default)
    {
        var items = await _taskGraphStore.ListAsync(ct).ConfigureAwait(true);
        var selectedId = TaskGraphs.FirstOrDefault(x => x.IsSelected)?.Id;
        TaskGraphs.Clear();
        _taskGraphsById.Clear();

        foreach (var item in items.OrderByDescending(x => x.UpdatedAt))
        {
            var vm = new SidebarTaskGraphItemViewModel(item)
            {
                IsSelected = string.Equals(item.Id, selectedId, StringComparison.Ordinal),
            };
            TaskGraphs.Add(vm);
            _taskGraphsById[item.Id] = vm;
        }

        OnPropertyChanged(nameof(HasTaskGraphs));
    }

    public async Task RenameTaskGraphAsync(string taskGraphId, string newName, CancellationToken ct = default)
    {
        var graph = await _taskGraphStore.LoadAsync(taskGraphId, ct).ConfigureAwait(true);
        if (graph is null)
        {
            return;
        }

        graph.Name = newName;
        await _taskGraphStore.SaveAsync(graph, ct).ConfigureAwait(true);
        await RefreshTaskGraphsAsync(ct).ConfigureAwait(true);
        SelectTaskGraph(taskGraphId);
    }

    public async Task RemoveTaskGraphAsync(string taskGraphId, CancellationToken ct = default)
    {
        await _taskGraphStore.DeleteAsync(taskGraphId, ct).ConfigureAwait(true);
        if (_taskGraphsById.Remove(taskGraphId, out var vm))
        {
            TaskGraphs.Remove(vm);
        }

        OnPropertyChanged(nameof(HasTaskGraphs));
    }

    public async Task ChangeTaskGraphProjectAsync(
        string taskGraphId,
        string? projectId,
        string? projectName,
        CancellationToken ct = default)
    {
        var graph = await _taskGraphStore.LoadAsync(taskGraphId, ct).ConfigureAwait(true);
        if (graph is null)
        {
            return;
        }

        graph.ProjectId = string.IsNullOrWhiteSpace(projectId) ? null : projectId;
        graph.ProjectName = string.IsNullOrWhiteSpace(projectName) ? null : projectName;
        await _taskGraphStore.SaveAsync(graph, ct).ConfigureAwait(true);
        await RefreshTaskGraphsAsync(ct).ConfigureAwait(true);
        SelectTaskGraph(taskGraphId);
    }

    /// <summary>Sets the focused project (used by the chat VM when it picks a project).</summary>
    public void SetCurrentProject(SidebarProjectViewModel? project)
    {
        if (CurrentProject is not null) CurrentProject.IsCurrent = false;
        CurrentProject = project;
        if (CurrentProject is not null) CurrentProject.IsCurrent = true;
        OnPropertyChanged(nameof(CurrentWorkingDirectory));
    }

    /// <summary>Restores the focused project by id (used at app startup).</summary>
    public void RestoreCurrentProject(string? projectId)
    {
        if (projectId is not null && _projectsById.TryGetValue(projectId, out var p))
        {
            SetCurrentProject(p);
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

    // ── Commands surfaced to the view ───────────────────────────────────

    [RelayCommand]
    private void SelectSession(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        ClearSelection();
        if (_sessionsById.TryGetValue(sessionId, out var vm))
        {
            vm.IsSelected = true;
            if (vm.ProjectId is not null && _projectsById.TryGetValue(vm.ProjectId, out var p))
            {
                SetCurrentProject(p);
            }
        }
        SessionSelected?.Invoke(this, sessionId);
    }

    [RelayCommand]
    private void RequestNewSession()
    {
        ClearSelection();
        NewSessionRequested?.Invoke(this, new NewSessionContext(CurrentWorkingDirectory));
    }

    [RelayCommand]
    private void RequestNewSessionInProject(string? projectId)
    {
        if (string.IsNullOrEmpty(projectId)) return;
        if (!_projectsById.TryGetValue(projectId, out var p)) return;
        SetCurrentProject(p);
        ClearSelection();
        NewSessionRequested?.Invoke(this, new NewSessionContext(p.Directory));
    }

    [RelayCommand]
    private void RequestTaskGraph() => TaskGraphRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void RequestNewTaskGraph() => NewTaskGraphRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenTaskGraph(string? taskGraphId)
    {
        if (string.IsNullOrEmpty(taskGraphId))
        {
            return;
        }
        TaskGraphOpenRequested?.Invoke(this, taskGraphId);
    }

    [RelayCommand]
    private void SelectTaskGraph(string? taskGraphId)
    {
        if (string.IsNullOrEmpty(taskGraphId))
        {
            return;
        }

        ClearSelection();
        foreach (var item in TaskGraphs)
        {
            item.IsSelected = string.Equals(item.Id, taskGraphId, StringComparison.Ordinal);
        }

        TaskGraphSelected?.Invoke(this, taskGraphId);
    }

    [RelayCommand]
    private void RequestTaskGraphAction(TaskGraphActionRequest request)
        => TaskGraphActionRequested?.Invoke(this, request);

    [RelayCommand]
    private void OpenSearchOverlay()
    {
        IsSearchOverlayVisible = true;
        RefreshSearchResults();
    }

    [RelayCommand]
    private void CloseSearchOverlay()
    {
        IsSearchOverlayVisible = false;
        SearchText = string.Empty;
        RefreshSearchResults();
    }

    [RelayCommand]
    private void RequestAddProject() => AddProjectRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void RequestProjectAction(ProjectActionRequest request)
        => ProjectActionRequested?.Invoke(this, request);

    [RelayCommand]
    private void RequestSessionAction(SessionActionRequest request)
        => SessionActionRequested?.Invoke(this, request);

    public void ToggleProjectsExpanded()
    {
        var expandAll = Projects.Any(p => !p.IsExpanded);
        foreach (var project in Projects)
        {
            project.IsExpanded = expandAll;
        }

        AreProjectsExpanded = expandAll;
    }

    public void SyncProjectsExpandedState()
    {
        AreProjectsExpanded = Projects.Count == 0 || Projects.All(p => p.IsExpanded);
    }

    public void ToggleOrphansExpanded()
    {
        IsOrphansExpanded = !IsOrphansExpanded;
    }

    public void ToggleTaskGraphsExpanded()
    {
        IsTaskGraphsExpanded = !IsTaskGraphsExpanded;
    }

    [RelayCommand]
    private void ToggleProjectExpanded(string? projectId)
    {
        if (string.IsNullOrEmpty(projectId)) return;
        if (_projectsById.TryGetValue(projectId, out var p))
        {
            p.IsExpanded = !p.IsExpanded;
            SyncProjectsExpandedState();
        }
    }

    [RelayCommand]
    private void SelectProject(string? projectId)
    {
        if (string.IsNullOrEmpty(projectId)) return;
        if (_projectsById.TryGetValue(projectId, out var p))
        {
            SetCurrentProject(p);
        }
    }

    private void ClearSelection()
    {
        foreach (var p in Projects)
        {
            foreach (var s in p.Sessions) s.IsSelected = false;
        }
        foreach (var s in _orphanSessions) s.IsSelected = false;
        foreach (var graph in TaskGraphs) graph.IsSelected = false;
    }

    partial void OnSearchTextChanged(string value)
    {
        RefreshSearchResults();
    }

    private void RefreshSearchResults()
    {
        SearchResults.Clear();

        var query = (SearchText ?? string.Empty).Trim();
        var sessions = _sessionsById.Values
            .OrderByDescending(s => s.Record.CreatedAt)
            .Where(s => query.Length == 0 || s.Title.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var session in sessions)
        {
            SearchResults.Add(session);
        }

        OnPropertyChanged(nameof(HasSearchResults));
    }
}

/// <summary>Context passed to MainWindow when the user requests a new session.</summary>
public sealed record NewSessionContext(string? WorkingDirectory);

/// <summary>Project-level "..." menu action.</summary>
public sealed record ProjectActionRequest(
    string ProjectId,
    ProjectActionKind Kind
);

public enum ProjectActionKind
{
    TogglePin,
    OpenInExplorer,
    Rename,
    Remove,
}

/// <summary>Session-level "..." menu action.</summary>
public sealed record SessionActionRequest(
    string SessionId,
    SessionActionKind Kind
);

public enum SessionActionKind
{
    OpenInExplorer,
    Rename,
    Remove,
}

public sealed record TaskGraphActionRequest(
    string TaskGraphId,
    TaskGraphActionKind Kind
);

public enum TaskGraphActionKind
{
    Rename,
    Remove,
    ChangeProject,
}
