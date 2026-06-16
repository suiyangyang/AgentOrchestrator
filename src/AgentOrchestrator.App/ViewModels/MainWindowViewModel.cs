using System;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.Sidebar;
using AgentOrchestrator.App.Services.Sidebar;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentOrchestrator.App.ViewModels;

/// <summary>
/// Top-level shell. Owns the two workspaces (Chat, TaskGraph) and the
/// Sidebar. Routes user actions from the sidebar to the right workspace
/// and back.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ISidebarRepository _repo;

    public MainWindowViewModel(
        ChatWorkspaceViewModel chat,
        TaskGraphWorkspaceViewModel graph,
        SidebarViewModel sidebar,
        SettingsViewModel settings,
        ISidebarRepository repo)
    {
        _repo = repo;
        Chat = chat;
        TaskGraph = graph;
        Sidebar = sidebar;
        Settings = settings;

        // Wire sidebar events into MainWindow so it can switch workspaces
        // and load sessions into the chat VM.
        Sidebar.SessionSelected += OnSidebarSessionSelected;
        Sidebar.NewSessionRequested += (_, _) => StartNewChat();
        Sidebar.TaskGraphRequested += (_, _) => ActiveWorkspace = TaskGraph;

        ActiveWorkspace = Chat;
        _ = InitializeAsync();
    }

    public ChatWorkspaceViewModel Chat { get; }
    public TaskGraphWorkspaceViewModel TaskGraph { get; }
    public SidebarViewModel Sidebar { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    private ViewModelBase _activeWorkspace = null!;

    private async Task InitializeAsync()
    {
        await Sidebar.LoadAsync();
    }

    private async void OnSidebarSessionSelected(object? sender, string sessionId)
    {
        // Switch to chat workspace and load the session.
        ActiveWorkspace = Chat;
        var record = await _repo.GetSessionAsync(sessionId);
        if (record is null) return;
        await Chat.OpenSessionAsync(record);
    }

    [RelayCommand]
    private void StartNewChat()
    {
        ActiveWorkspace = Chat;
        Chat.OpenBlankPage();
    }

    [RelayCommand]
    private void OpenTaskGraph()
    {
        ActiveWorkspace = TaskGraph;
    }
}
