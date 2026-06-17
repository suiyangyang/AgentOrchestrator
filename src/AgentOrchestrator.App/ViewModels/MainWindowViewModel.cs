using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.Sidebar;
using AgentOrchestrator.App.Services.Settings;
using AgentOrchestrator.App.Services.Sidebar;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentOrchestrator.App.ViewModels;

/// <summary>
/// Top-level shell. Owns the two workspaces (Chat, TaskGraph) and the
/// Sidebar. Routes user actions from the sidebar to the right workspace
/// and back, and orchestrates cross-cutting flows (folder picker, file
/// explorer, rename dialog, remove confirmation, settings persistence).
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ISidebarRepository _repo;
    private readonly IAppSettingsService _settingsService;

    public MainWindowViewModel(
        ChatWorkspaceViewModel chat,
        TaskGraphWorkspaceViewModel graph,
        SidebarViewModel sidebar,
        SettingsViewModel settings,
        ISidebarRepository repo,
        IAppSettingsService settingsService)
    {
        _repo = repo;
        _settingsService = settingsService;
        Chat = chat;
        TaskGraph = graph;
        Sidebar = sidebar;
        Settings = settings;

        Sidebar.SessionSelected += OnSidebarSessionSelected;
        Sidebar.NewSessionRequested += OnSidebarNewSessionRequested;
        Sidebar.TaskGraphRequested += (_, _) => ActiveWorkspace = TaskGraph;
        Sidebar.AddProjectRequested += (_, _) => _ = OnAddProjectRequested();
        Sidebar.ProjectActionRequested += OnProjectActionRequested;
        Sidebar.SessionActionRequested += OnSessionActionRequested;

        // Persist focus changes back to the settings file so the next
        // launch can restore the same working directory.
        Sidebar.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SidebarViewModel.CurrentProject))
            {
                _settingsService.Save(BuildSettingsSnapshot());
            }
        };

        ActiveWorkspace = Chat;
        _ = InitializeAsync();
    }

    public ChatWorkspaceViewModel Chat { get; }
    public TaskGraphWorkspaceViewModel TaskGraph { get; }
    public SidebarViewModel Sidebar { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    private ViewModelBase _activeWorkspace = null!;

    /// <summary>The MainWindow sets this on Opened so dialogs and pickers can find it.</summary>
    public IStorageProvider? Storage { get; set; }

    private AppSettings BuildSettingsSnapshot() => new()
    {
        OpenCodeCliPath = Settings.OpenCodeCliPath,
        Host = Settings.Host,
        Port = Settings.Port,
        Username = Settings.Username,
        Password = Settings.Password,
        LastProjectId = Sidebar.CurrentProject?.Id,
    };

    private async Task InitializeAsync()
    {
        await Sidebar.LoadAsync();

        // Restore last focused project so the blank page uses the same
        // working directory as the previous session.
        var settings = _settingsService.Load();
        if (!string.IsNullOrEmpty(settings.LastProjectId))
        {
            Sidebar.RestoreCurrentProject(settings.LastProjectId);
        }
    }

    // ── Sidebar event handlers ─────────────────────────────────────────

    private async void OnSidebarSessionSelected(object? sender, string sessionId)
    {
        ActiveWorkspace = Chat;
        var record = await _repo.GetSessionAsync(sessionId);
        if (record is null) return;
        await Chat.OpenSessionAsync(record);
    }

    private void OnSidebarNewSessionRequested(object? sender, NewSessionContext ctx)
    {
        ActiveWorkspace = Chat;
        Chat.OpenBlankPage(ctx.WorkingDirectory);
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

    private bool TryGetProject(string projectId, out SidebarProjectViewModel pvm)
    {
        foreach (var p in Sidebar.Projects)
        {
            if (p.Id == projectId) { pvm = p; return true; }
        }
        pvm = null!;
        return false;
    }

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
}
