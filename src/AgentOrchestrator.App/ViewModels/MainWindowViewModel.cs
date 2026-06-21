using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.Sidebar;
using AgentOrchestrator.App.Services.Agent;
using AgentOrchestrator.App.Services.Settings;
using AgentOrchestrator.App.Services.Sidebar;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using AgentOrchestrator.App.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace AgentOrchestrator.App.ViewModels;

/// <summary>
/// Top-level shell. Owns the two workspaces (Chat, TaskGraph) and the
/// Sidebar. Routes user actions from the sidebar to the right workspace
/// and back, and orchestrates cross-cutting flows (folder picker, file
/// explorer, rename dialog, remove confirmation, settings persistence).
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private static readonly GridLength DefaultLeftSidebarWidth = new(300);
    private static readonly GridLength DefaultRightSidebarWidth = new(400);

    private readonly ISidebarRepository _repo;
    private readonly IAppSettingsService _settingsService;
    private readonly IServiceProvider _services;
    private readonly IAgentGateway _agent;
    private GridLength _leftSidebarExpandedWidth = DefaultLeftSidebarWidth;
    private GridLength _rightSidebarExpandedWidth = DefaultRightSidebarWidth;
    private bool _isRestoringTaskGraphShell;
    private StartupOptions? _startupOptions;

    public MainWindowViewModel(
        ChatWorkspaceViewModel chat,
        TaskGraphWorkspaceViewModel graph,
        SidebarViewModel sidebar,
        SettingsViewModel settings,
        ISidebarRepository repo,
        IAppSettingsService settingsService,
        IServiceProvider services,
        IAgentGateway agent)
    {
        _repo = repo;
        _settingsService = settingsService;
        _services = services;
        _agent = agent;
        Chat = chat;
        TaskGraph = graph;
        Sidebar = sidebar;
        Settings = settings;

        Sidebar.SessionSelected += OnSidebarSessionSelected;
        Sidebar.NewSessionRequested += OnSidebarNewSessionRequested;
        Sidebar.TaskGraphRequested += (_, _) => ActiveWorkspace = TaskGraph;
        Sidebar.TaskGraphSelected += OnSidebarTaskGraphSelected;
        Sidebar.TaskGraphOpenRequested += OnSidebarTaskGraphOpenRequested;
        Sidebar.NewTaskGraphRequested += OnSidebarNewTaskGraphRequested;
        Sidebar.AddProjectRequested += (_, _) => _ = OnAddProjectRequested();
        Sidebar.ProjectActionRequested += OnProjectActionRequested;
        Sidebar.SessionActionRequested += OnSessionActionRequested;
        Sidebar.TaskGraphActionRequested += OnTaskGraphActionRequested;
        Sidebar.SessionSelected += OnSidebarSessionSelectionChanged;

        // Persist focus changes back to the settings file so the next
        // launch can restore the same working directory.
        Sidebar.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SidebarViewModel.CurrentProject))
            {
                _settingsService.Save(BuildSettingsSnapshot());
            }
        };

        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SettingsViewModel.OpenCodeEnabled)
                or nameof(SettingsViewModel.IsOpenCodeConnected))
            {
                UpdateConnectedServiceCount();
            }
        };

        Chat.PropertyChanged += OnWorkspacePropertyChanged;
        TaskGraph.PropertyChanged += OnWorkspacePropertyChanged;
        TaskGraph.NodeDetailRequested += OnTaskGraphNodeDetailRequested;

        _agent.AgentErrorOccurred += OnAgentErrorOccurred;

        ActiveWorkspace = Chat;
        UpdateConnectedServiceCount();
        _ = InitializeAsync();
    }

    /// <summary>
    /// Optional startup options supplied via command-line flags. When set, the
    /// shell will auto-open the referenced task graph (and optionally maximize
    /// the graph workspace) after the sidebar finishes loading.
    /// </summary>
    public StartupOptions? StartupOptions
    {
        get => _startupOptions;
        set => _startupOptions = value;
    }

    public ChatWorkspaceViewModel Chat { get; }
    public TaskGraphWorkspaceViewModel TaskGraph { get; }
    public SidebarViewModel Sidebar { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    private ViewModelBase _activeWorkspace = null!;

    [ObservableProperty]
    private GridLength _leftSidebarWidth = DefaultLeftSidebarWidth;

    [ObservableProperty]
    private GridLength _rightSidebarWidth = DefaultRightSidebarWidth;

    [ObservableProperty]
    private bool _isLeftSidebarVisible = true;

    [ObservableProperty]
    private bool _isRightSidebarVisible = true;

    [ObservableProperty]
    private bool _isTaskOrchestrationVisible = true;

    [ObservableProperty]
    private int _connectedServiceCount;

    [ObservableProperty]
    private string? _agentErrorMessage;

    /// <summary>Derived visibility toggle for the error banner.</summary>
    public bool HasAgentError => !string.IsNullOrEmpty(AgentErrorMessage);

    partial void OnAgentErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasAgentError));
    }

    private void OnAgentErrorOccurred(object? sender, AgentErrorEventArgs e)
    {
        AgentErrorMessage = e.UserMessage;
    }

    [RelayCommand]
    private void DismissAgentError()
    {
        AgentErrorMessage = null;
    }

    public bool HasConnectedServices => ConnectedServiceCount > 0;

    /// <summary>True when the active workspace is the chat. Drives the right
    /// sidebar to show the Subagent panel.</summary>
    public bool IsChatMode => ActiveWorkspace == Chat;

    /// <summary>True when the active workspace is the task graph. Drives the
    /// right sidebar to show node details + bug report instead of the chat
    /// subagent panel.</summary>
    public bool IsTaskGraphMode => ActiveWorkspace == TaskGraph;

    /// <summary>The MainWindow sets this on Opened so dialogs and pickers can find it.</summary>
    public IStorageProvider? Storage { get; set; }

    private AppSettings BuildSettingsSnapshot() => new()
    {
        OpenCodeEnabled = Settings.OpenCodeEnabled,
        Host = Settings.Host,
        Port = Settings.Port,
        Username = Settings.Username,
        Password = Settings.Password,
        LastProjectId = Sidebar.CurrentProject?.Id,
    };

    private async Task InitializeAsync()
    {
        await Sidebar.LoadAsync();
        await Settings.InitializeAsync();
        UpdateConnectedServiceCount();

        // Restore last focused project so the blank page uses the same
        // working directory as the previous session.
        var settings = _settingsService.Load();
        if (!string.IsNullOrEmpty(settings.LastProjectId))
        {
            Sidebar.RestoreCurrentProject(settings.LastProjectId);
        }

        // Drive any non-interactive auto-open (e.g. for screenshot tests).
        if (_startupOptions is { OpenGraphToken: { Length: > 0 } token })
        {
            await OpenTaskGraphByTokenAsync(token, _startupOptions.MaximizeGraph).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Resolves a task graph by id / 1-based index / name (same lookup order
    /// used by the CLI's <c>taskgraph select</c>) and loads it into the graph
    /// workspace. Optionally maximizes the workspace so the graph fills the
    /// whole window — this is the canonical "select from sidebar → display in
    /// graph → fullscreen" flow exercised by tests.
    /// </summary>
    public async Task OpenTaskGraphByTokenAsync(string token, bool maximize)
    {
        var store = _services.GetRequiredService<Services.TaskGraph.ITaskGraphStore>();
        Models.TaskGraph.TaskGraph? graph = null;

        // 1) Direct id.
        graph = await store.LoadAsync(token).ConfigureAwait(true);

        // 2) 1-based index — mirrors `taskgraph list` output.
        if (graph is null && int.TryParse(token, out var index) && index > 0)
        {
            var items = await store.ListAsync().ConfigureAwait(true);
            if (index <= items.Count)
            {
                graph = await store.LoadAsync(items[index - 1].Id).ConfigureAwait(true);
            }
        }

        // 3) Name match.
        if (graph is null)
        {
            var items = await store.ListAsync().ConfigureAwait(true);
            var match = items.FirstOrDefault(x => string.Equals(x.Name, token, StringComparison.OrdinalIgnoreCase))
                        ?? items.FirstOrDefault(x => x.Name.Contains(token, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                graph = await store.LoadAsync(match.Id).ConfigureAwait(true);
            }
        }

        if (graph is null)
        {
            return;
        }

        // Ensure sidebar reflects the selected graph and refreshes its tree.
        await Sidebar.RefreshTaskGraphsAsync().ConfigureAwait(true);
        Sidebar.SelectTaskGraphCommand.Execute(graph.Id);

        ActiveWorkspace = TaskGraph;
        await TaskGraph.OpenGraphByIdAsync(graph.Id).ConfigureAwait(true);

        if (maximize && !TaskGraph.IsGraphMaximized)
        {
            TaskGraph.ToggleGraphMaximizeCommand.Execute(null);
        }
    }

    partial void OnLeftSidebarWidthChanged(GridLength value)
    {
        if (IsLeftSidebarVisible && value.Value > 0)
        {
            _leftSidebarExpandedWidth = value;
        }
    }

    partial void OnRightSidebarWidthChanged(GridLength value)
    {
        if (IsRightSidebarVisible && value.Value > 0)
        {
            _rightSidebarExpandedWidth = value;
        }
    }

    partial void OnIsLeftSidebarVisibleChanged(bool value)
    {
        if (!value)
        {
            if (LeftSidebarWidth.Value > 0)
            {
                _leftSidebarExpandedWidth = LeftSidebarWidth;
            }

            LeftSidebarWidth = new GridLength(0);
            return;
        }

        LeftSidebarWidth = NormalizeRestoredWidth(_leftSidebarExpandedWidth, DefaultLeftSidebarWidth);
    }

    partial void OnIsRightSidebarVisibleChanged(bool value)
    {
        if (!value)
        {
            if (RightSidebarWidth.Value > 0)
            {
                _rightSidebarExpandedWidth = RightSidebarWidth;
            }

            RightSidebarWidth = new GridLength(0);
            return;
        }

        RightSidebarWidth = NormalizeRestoredWidth(_rightSidebarExpandedWidth, DefaultRightSidebarWidth);
    }

    private static GridLength NormalizeRestoredWidth(GridLength value, GridLength fallback)
        => value.Value > 0 ? value : fallback;

    partial void OnActiveWorkspaceChanged(ViewModelBase value)
    {
        OnPropertyChanged(nameof(ActiveWorkspaceTitle));
        OnPropertyChanged(nameof(IsChatMode));
        OnPropertyChanged(nameof(IsTaskGraphMode));

        if (value == TaskGraph)
        {
            SyncTaskGraphShellFocusMode();
            return;
        }

        if (TaskGraph.IsGraphMaximized)
        {
            TaskGraph.ToggleGraphMaximizeCommand.Execute(null);
        }
    }

    partial void OnConnectedServiceCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasConnectedServices));
    }

    private void OnWorkspacePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender == Chat && e.PropertyName == nameof(ChatWorkspaceViewModel.HeaderTitle))
        {
            OnPropertyChanged(nameof(ActiveWorkspaceTitle));
            return;
        }

        if (sender == TaskGraph)
        {
            if (e.PropertyName == nameof(TaskGraphWorkspaceViewModel.HeaderTitle))
            {
                OnPropertyChanged(nameof(ActiveWorkspaceTitle));
            }

            if (e.PropertyName == nameof(TaskGraphWorkspaceViewModel.IsGraphMaximized))
            {
                SyncTaskGraphShellFocusMode();
            }
        }
    }

    private void SyncTaskGraphShellFocusMode()
    {
        if (ActiveWorkspace != TaskGraph && !TaskGraph.IsGraphMaximized)
        {
            return;
        }

        if (TaskGraph.IsGraphMaximized)
        {
            IsLeftSidebarVisible = false;
            IsRightSidebarVisible = false;
            return;
        }

        if (_isRestoringTaskGraphShell)
        {
            return;
        }

        _isRestoringTaskGraphShell = true;
        try
        {
            IsLeftSidebarVisible = true;
            IsRightSidebarVisible = true;
        }
        finally
        {
            _isRestoringTaskGraphShell = false;
        }
    }

    private void UpdateConnectedServiceCount()
    {
        ConnectedServiceCount = Settings.OpenCodeEnabled && Settings.IsOpenCodeConnected ? 1 : 0;
    }

    private async void OnTaskGraphNodeDetailRequested(object? sender, TaskGraphNodeDetailRequest request)
    {
        var viewModel = _services.GetRequiredService<TaskGraphNodeDetailViewModel>();
        await viewModel.InitializeAsync(request.GraphId, request.Node, request.WorkingDirectory).ConfigureAwait(true);
        var window = new TaskGraphNodeDetailWindow(viewModel);
        var owner = GetOwnerWindow();
        if (owner is not null)
        {
            await window.ShowDialog(owner).ConfigureAwait(true);
        }
        else
        {
            window.Show();
        }

        viewModel.Dispose();
    }

    // ── Sidebar event handlers ─────────────────────────────────────────

    private async void OnSidebarSessionSelected(object? sender, string sessionId)
    {
        ActiveWorkspace = Chat;
        var record = await _repo.GetSessionAsync(sessionId);
        if (record is null) return;
        await Chat.OpenSessionAsync(record);
    }

    private void OnSidebarSessionSelectionChanged(object? sender, string sessionId)
    {
        Sidebar.CloseSearchOverlayCommand.Execute(null);
    }

    private void OnSidebarNewSessionRequested(object? sender, NewSessionContext ctx)
    {
        ActiveWorkspace = Chat;
        Chat.OpenBlankPage(ctx.WorkingDirectory);
    }

    private async void OnSidebarTaskGraphSelected(object? sender, string taskGraphId)
    {
        ActiveWorkspace = TaskGraph;
        await TaskGraph.OpenGraphByIdAsync(taskGraphId);
    }

    private async void OnSidebarTaskGraphOpenRequested(object? sender, string taskGraphId)
    {
        ActiveWorkspace = TaskGraph;
        await TaskGraph.OpenGraphByIdAsync(taskGraphId);
    }

    private async void OnSidebarNewTaskGraphRequested(object? sender, EventArgs e)
    {
        ActiveWorkspace = TaskGraph;
        await TaskGraph.NewGraphCommand.ExecuteAsync(null);
    }

    private async Task OnAddProjectRequested()
    {
        if (Storage is null) return;
        var folders = await Storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择工作目录",
            AllowMultiple = false,
        });
        if (folders.Count == 0) return;
        var path = folders[0].Path.LocalPath;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
        var name = new DirectoryInfo(path).Name;
        if (string.IsNullOrWhiteSpace(name)) name = path;
        await Sidebar.AddProjectAsync(name, path);
    }

    private async void OnProjectActionRequested(object? sender, ProjectActionRequest req)
    {
        if (!TryGetProject(req.ProjectId, out var p)) return;

        switch (req.Kind)
        {
            case ProjectActionKind.TogglePin:
                p.IsPinned = !p.IsPinned;
                break;
            case ProjectActionKind.OpenInExplorer:
                OpenInExplorer(p.Directory);
                break;
            case ProjectActionKind.Rename:
            {
                var newName = await PromptInputAsync("重命名项目", "新名称", p.Name);
                if (!string.IsNullOrWhiteSpace(newName))
                {
                    await Sidebar.RenameProjectAsync(p.Id, newName);
                }
                break;
            }
            case ProjectActionKind.Remove:
            {
                var ok = await PromptConfirmAsync(
                    "移除项目",
                    $"确定要从侧边栏移除项目 “{p.Name}” 吗?\n（不会删除本地文件,仅清除本地记录）");
                if (ok)
                {
                    await Sidebar.RemoveProjectAsync(p.Id);
                }
                break;
            }
        }
    }

    private async void OnSessionActionRequested(object? sender, SessionActionRequest req)
    {
        switch (req.Kind)
        {
            case SessionActionKind.Rename:
            {
                var record = await _repo.GetSessionAsync(req.SessionId);
                if (record is null) return;
                var newTitle = await PromptInputAsync("重命名对话", "新标题", record.Title);
                if (!string.IsNullOrWhiteSpace(newTitle))
                {
                    var updated = record with { Title = newTitle };
                    await _repo.UpdateSessionAsync(updated);
                    Sidebar.UpdateSessionTitle(req.SessionId, newTitle);
                }
                break;
            }
            case SessionActionKind.Remove:
            {
                var record = await _repo.GetSessionAsync(req.SessionId);
                if (record is null) return;
                var ok = await PromptConfirmAsync("移除对话", $"确定要移除对话 “{record.Title}” 吗?");
                if (ok)
                {
                    await Sidebar.RemoveSessionAsync(req.SessionId);
                }
                break;
            }
            case SessionActionKind.OpenInExplorer:
            {
                var record = await _repo.GetSessionAsync(req.SessionId);
                if (record is not null && !string.IsNullOrEmpty(record.ProjectId)
                    && TryGetProject(record.ProjectId, out var p))
                {
                    OpenInExplorer(p.Directory);
                }
                break;
            }
        }
    }

    private async void OnTaskGraphActionRequested(object? sender, TaskGraphActionRequest req)
    {
        switch (req.Kind)
        {
            case TaskGraphActionKind.Rename:
            {
                var graph = await TaskGraphStoreLoadAsync(req.TaskGraphId);
                if (graph is null)
                {
                    return;
                }

                var newName = await PromptInputAsync("重命名任务编排", "新名称", graph.Name);
                if (!string.IsNullOrWhiteSpace(newName))
                {
                    await Sidebar.RenameTaskGraphAsync(req.TaskGraphId, newName);
                }
                break;
            }
            case TaskGraphActionKind.Remove:
            {
                var graph = await TaskGraphStoreLoadAsync(req.TaskGraphId);
                if (graph is null)
                {
                    return;
                }

                var ok = await PromptConfirmAsync("移除任务编排", $"确定要移除任务编排 “{graph.Name}” 吗?");
                if (ok)
                {
                    await Sidebar.RemoveTaskGraphAsync(req.TaskGraphId);
                    if (TaskGraph.CurrentGraph?.Id == req.TaskGraphId)
                    {
                        await TaskGraph.NewGraphCommand.ExecuteAsync(null);
                    }
                }
                break;
            }
        }
    }

    private bool TryGetProject(string projectId, out SidebarProjectViewModel pvm)
    {
        foreach (var p in Sidebar.Projects)
        {
            if (p.Id == projectId) { pvm = p; return true; }
        }
        pvm = null!;
        return false;
    }

    private Task<Models.TaskGraph.TaskGraph?> TaskGraphStoreLoadAsync(string taskGraphId)
        => _services.GetRequiredService<Services.TaskGraph.ITaskGraphStore>().LoadAsync(taskGraphId);

    private static void OpenInExplorer(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch
        {
            // Best-effort; no surface to the user for v1.
        }
    }

    // ── Simple modal dialog helpers ────────────────────────────────────

    private Task<bool> PromptConfirmAsync(string title, string message) =>
        DialogHost.ConfirmAsync(GetOwnerWindow(), title, message);

    private Task<string?> PromptInputAsync(string title, string label, string initial) =>
        DialogHost.InputAsync(GetOwnerWindow(), title, label, initial);

    private static Window? GetOwnerWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }

    [RelayCommand]
    private void StartNewChat()
    {
        ActiveWorkspace = Chat;
        Chat.OpenBlankPage(Sidebar.CurrentWorkingDirectory);
    }

    [RelayCommand]
    private void OpenTaskGraph()
    {
        ActiveWorkspace = TaskGraph;
    }

    [RelayCommand]
    private void ToggleLeftSidebar()
    {
        IsLeftSidebarVisible = !IsLeftSidebarVisible;
    }

    [RelayCommand]
    private void ToggleRightSidebar()
    {
        IsRightSidebarVisible = !IsRightSidebarVisible;
    }

    public string ActiveWorkspaceTitle => ActiveWorkspace switch
    {
        ChatWorkspaceViewModel chat => chat.HeaderTitle,
        TaskGraphWorkspaceViewModel graph => graph.HeaderTitle,
        _ => "工作区",
    };
}
