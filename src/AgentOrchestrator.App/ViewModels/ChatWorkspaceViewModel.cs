using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.Chat;
using AgentOrchestrator.App.Models.Sidebar;
using AgentOrchestrator.App.Services.Agent;
using AgentOrchestrator.App.Services.Sidebar;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentChatRequest = AgentOrchestrator.App.Services.Agent.ChatRequest;

namespace AgentOrchestrator.App.ViewModels;

/// <summary>
/// Chat workspace. Holds the active session's <see cref="Messages"/>,
/// the draft composer text, the selected permission and model, and the
/// pending attachment list.
///
/// Lifecycle (per Docs/working/侧边导航栏与会话管理-方案.md §6):
/// 1. New session starts in "blank page" mode (CurrentSessionId == null, Messages empty).
/// 2. First Send on a blank page creates a session via IAgentGateway, writes
///    a SessionRecord to SQLite, and emits it to the sidebar.
/// 3. Subsequent Sends reuse the existing session.
/// 4. Streaming chunks from IAgentGateway append to the assistant message in real time.
/// </summary>
public partial class ChatWorkspaceViewModel : ViewModelBase
{
    private readonly IAgentGateway _agent;
    private readonly ISidebarRepository _repo;
    private readonly SidebarViewModel _sidebar;
    private CancellationTokenSource? _sendCts;

    public ChatWorkspaceViewModel(
        IAgentGateway agent,
        ISidebarRepository repo,
        SidebarViewModel sidebar)
    {
        _agent = agent;
        _repo = repo;
        _sidebar = sidebar;

        _selectedPermission = Permissions[2];
        _selectedPermission.IsSelected = true;
        Attachments.CollectionChanged += OnAttachmentsChanged;
    }

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];
    public ObservableCollection<ChatAttachment> Attachments { get; } = [];
    public ObservableCollection<PermissionOption> Permissions { get; } =
    [
        new("ask", "请求批准", "编辑外部文件和使用互联网时始终询问", "✋"),
        new("replace", "替代批准", "仅对检测到的风险操作请求批准", "◔"),
        new("full", "完全访问权限", "可不受限制地访问互联网和您电脑上的任何文件", "🛡")
    ];
    public IReadOnlyList<string> Models { get; } = ["codex", "gpt-5", "claude-compatible"];

    /// <summary>The local session id. Null = blank page.</summary>
    [ObservableProperty]
    private string? _currentSessionId;

    /// <summary>The Agent-side session id. Null when CurrentSessionId is null.</summary>
    [ObservableProperty]
    private string? _currentAgentSessionId;

    [ObservableProperty]
    private string _draftText = string.Empty;

    [ObservableProperty]
    private PermissionOption _selectedPermission = null!;

    [ObservableProperty]
    private string _selectedModel = "codex";

    [ObservableProperty]
    private bool _isStreaming;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string _headerTitle = "新对话";

    public bool HasAttachments => Attachments.Count > 0;
    public bool IsBlankPage => CurrentSessionId is null && Messages.Count == 0;

    /// <summary>Fired when a brand-new session is created (so MainWindow can switch workspace).</summary>
    public event EventHandler? SessionChanged;

    partial void OnSelectedPermissionChanged(PermissionOption value)
    {
        foreach (var item in Permissions) item.IsSelected = ReferenceEquals(item, value);
    }

    private void OnAttachmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(HasAttachments));

    // ── Public session lifecycle (called by MainWindowViewModel) ─────────

    /// <summary>
    /// Opens an existing session. Resets the message list, then asynchronously
    /// loads the message history from the Agent.
    /// </summary>
    public async Task OpenSessionAsync(SessionRecord record, CancellationToken ct = default)
    {
        _sendCts?.Cancel();
        SendCancellationCleanup();

        CurrentSessionId = record.SessionId;
        CurrentAgentSessionId = record.AgentSessionId;
        HeaderTitle = string.IsNullOrWhiteSpace(record.Title) ? "新对话" : record.Title;
        Messages.Clear();
        StatusMessage = "正在加载历史…";

        try
        {
            var remote = await _agent.GetMessagesAsync(record.AgentSessionId, ct).ConfigureAwait(true);
            foreach (var msg in remote)
            {
                Messages.Add(MapRemoteMessage(msg));
            }
            StatusMessage = null;
        }
        catch (Exception ex)
        {
            // Per spec: do NOT clear messages on failure. Show error.
            StatusMessage = $"加载历史失败:{ex.Message}";
        }
        OnPropertyChanged(nameof(IsBlankPage));
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Switches the workspace back to a blank page.</summary>
    /// <param name="workingDirectory">Optional directory to use as the
    /// working directory when the first message is sent. If null, falls
    /// back to <see cref="ProjectsTracker"/> or the app base directory.</param>
    public void OpenBlankPage(string? workingDirectory = null)
    {
        _sendCts?.Cancel();
        SendCancellationCleanup();

        CurrentSessionId = null;
        CurrentAgentSessionId = null;
        HeaderTitle = "新对话";
        Messages.Clear();
        StatusMessage = null;

        if (!string.IsNullOrEmpty(workingDirectory))
        {
            ProjectsTracker.CurrentWorkingDirectory = workingDirectory;
        }

        OnPropertyChanged(nameof(IsBlankPage));
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Send (the heart of this VM) ──────────────────────────────────────

    [RelayCommand]
    private async Task SendAsync()
    {
        var prompt = DraftText.Trim();
        if (string.IsNullOrWhiteSpace(prompt) && Attachments.Count == 0) return;
        if (IsStreaming) return;

        // 1. Build the user message locally so the UI reflects it immediately.
        var userMessage = new ChatMessageViewModel(Guid.NewGuid().ToString("N"), ChatRole.User, "你");
        if (!string.IsNullOrWhiteSpace(prompt))
        {
            userMessage.Blocks.Add(new ChatBlockViewModel(ChatBlockKind.Text, prompt));
        }
        foreach (var attachment in Attachments)
        {
            userMessage.Blocks.Add(new ChatBlockViewModel(ChatBlockKind.Image, attachment.DisplayName, attachment.Path));
        }
        Messages.Add(userMessage);

        // Snapshot the composer state and clear it BEFORE any async work.
        var snapshotAttachments = Attachments.ToList();
        DraftText = string.Empty;
        Attachments.Clear();

        // 2. Placeholder assistant message that streaming chunks will append to.
        var assistantId = Guid.NewGuid().ToString("N");
        var assistantMessage = new ChatMessageViewModel(assistantId, ChatRole.Assistant, "Codex")
        {
            IsStreaming = true,
        };
        Messages.Add(assistantMessage);
        IsStreaming = true;
        StatusMessage = "正在发送…";

        _sendCts = new CancellationTokenSource();
        var ct = _sendCts.Token;

        try
        {
            // 3. Resolve / create session. Blank page -> first-create.
            if (CurrentAgentSessionId is null)
            {
                var workingDir = ResolveWorkingDirectory();
                var record = await CreateSessionForFirstMessageAsync(workingDir, ct).ConfigureAwait(true);
                CurrentSessionId = record.SessionId;
                CurrentAgentSessionId = record.AgentSessionId;
                HeaderTitle = record.Title;
                _sidebar.AddOrUpdateSession(record);
                OnPropertyChanged(nameof(IsBlankPage));
            }

            // 4. Build ChatRequest and stream chunks into the assistant message.
            var chatRequest = new AgentChatRequest(
                Prompt: prompt,
                Attachments: snapshotAttachments,
                Permission: SelectedPermission.Key,
                Model: SelectedModel);

            await foreach (var chunk in _agent
                .SendMessageAsync(CurrentAgentSessionId!, chatRequest, ct)
                .ConfigureAwait(true))
            {
                ApplyChunk(assistantMessage, chunk);
            }

            // 5. Title sync (best-effort).
            await TrySyncTitleAsync(ct).ConfigureAwait(true);
            StatusMessage = null;
        }
        catch (OperationCanceledException)
        {
            // leave IsStreaming=true; ChatMessageViewModel already shows the partial.
            StatusMessage = "已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"发送失败:{ex.Message}";
        }
        finally
        {
            assistantMessage.IsStreaming = false;
            IsStreaming = false;
            SendCancellationCleanup();
        }
    }

    private void SendCancellationCleanup()
    {
        _sendCts?.Dispose();
        _sendCts = null;
    }

    [RelayCommand]
    private void Cancel()
    {
        _sendCts?.Cancel();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private string ResolveWorkingDirectory()
    {
        // If the user has a "current project" selected, use its directory;
        // otherwise default to the app's base directory.
        var current = ProjectsTracker.CurrentWorkingDirectory;
        return current ?? AppContext.BaseDirectory;
    }

    private async Task<SessionRecord> CreateSessionForFirstMessageAsync(string workingDir, CancellationToken ct)
    {
        // 1. Look up existing project for workingDir, or auto-create one.
        var project = await FindOrCreateProjectAsync(workingDir, ct).ConfigureAwait(true);

        // 2. Create Agent-side session.
        var agentSessionId = await _agent.CreateSessionAsync(
            new SessionCreateRequest(WorkingDirectory: workingDir, Title: null),
            ct).ConfigureAwait(true);

        // 3. Write local SessionRecord.
        var record = new SessionRecord(
            SessionId: Guid.NewGuid().ToString("N"),
            AgentSessionId: agentSessionId,
            Title: "新对话",
            ProjectId: project?.Id,
            CreatedAt: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await _repo.CreateSessionAsync(record, ct).ConfigureAwait(true);
        return record;
    }

    private async Task<ProjectRecord?> FindOrCreateProjectAsync(string workingDir, CancellationToken ct)
    {
        var projects = await _repo.ListProjectsAsync(ct).ConfigureAwait(true);
        var existing = projects.FirstOrDefault(p => string.Equals(
            NormalizeDir(p.Directory), NormalizeDir(workingDir), StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        var name = new DirectoryInfo(workingDir).Name;
        if (string.IsNullOrWhiteSpace(name)) name = workingDir;
        return await _repo.CreateProjectAsync(name, workingDir, ct).ConfigureAwait(true);
    }

    private static string NormalizeDir(string dir) =>
        dir.TrimEnd('/', '\\').Replace('/', '\\');

    private async Task TrySyncTitleAsync(CancellationToken ct)
    {
        if (CurrentAgentSessionId is null || CurrentSessionId is null) return;
        try
        {
            var newTitle = await _agent.GetSessionTitleAsync(CurrentAgentSessionId, ct).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(newTitle)) return;
            if (newTitle == "新对话") return;

            _sidebar.UpdateSessionTitle(CurrentSessionId, newTitle);
            HeaderTitle = newTitle;
            var existing = await _repo.GetSessionAsync(CurrentSessionId, ct).ConfigureAwait(true);
            if (existing is not null)
            {
                var updated = existing with { Title = newTitle };
                await _repo.UpdateSessionAsync(updated, ct).ConfigureAwait(true);
            }
        }
        catch
        {
            // Silent fallback per spec §6.5.
        }
    }

    private void ApplyChunk(ChatMessageViewModel assistant, ChatStreamChunk chunk)
    {
        var block = assistant.Blocks.LastOrDefault();
        if (block is null || !CanAppendToBlock(block, chunk))
        {
            block = CreateBlockForChunk(chunk);
            if (block is null) return;
            assistant.Blocks.Add(block);
        }

        var index = assistant.Blocks.IndexOf(block);
        if (index < 0) return;

        switch (chunk.Kind)
        {
            case ChatBlockKind.Text:
            case ChatBlockKind.Thought:
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    var combined = (block.Text ?? string.Empty) + chunk.Content;
                    assistant.Blocks[index] = new ChatBlockViewModel(block.Kind, combined, isExpanded: block.IsExpanded);
                }
                break;

            case ChatBlockKind.Image:
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    assistant.Blocks[index] = new ChatBlockViewModel(ChatBlockKind.Image, chunk.Content, isExpanded: block.IsExpanded);
                }
                break;

            case ChatBlockKind.Tool:
                {
                    var (toolName, toolState, toolOutput) = ParseToolChunk(block, chunk);
                    assistant.Blocks[index] = new ChatBlockViewModel(toolName, toolState, toolOutput, isExpanded: block.IsExpanded);
                }
                break;
        }
    }

    private static ChatBlockViewModel? CreateBlockForChunk(ChatStreamChunk chunk)
    {
        return chunk.Kind switch
        {
            ChatBlockKind.Text => new ChatBlockViewModel(ChatBlockKind.Text, string.Empty),
            ChatBlockKind.Thought => new ChatBlockViewModel(ChatBlockKind.Thought, string.Empty, isExpanded: false),
            ChatBlockKind.Tool => new ChatBlockViewModel(ExtractToolNameFromChunk(chunk.Content), ToolState.Running, string.Empty, isExpanded: false),
            ChatBlockKind.Image => new ChatBlockViewModel(ChatBlockKind.Image, chunk.Content),
            _ => null,
        };
    }

    private static bool CanAppendToBlock(ChatBlockViewModel block, ChatStreamChunk chunk)
    {
        if (block.Kind != chunk.Kind)
        {
            return false;
        }

        if (chunk.Kind != ChatBlockKind.Tool)
        {
            return true;
        }

        return string.Equals(
            block.ToolName ?? string.Empty,
            ExtractToolNameFromChunk(chunk.Content),
            StringComparison.Ordinal);
    }

    private static (string name, ToolState state, string? output) ParseToolChunk(ChatBlockViewModel block, ChatStreamChunk chunk)
    {
        var name = block.ToolName ?? ExtractToolName(chunk.Content);
        var state = block.ToolState;
        var output = block.ToolOutput;
        if (chunk.Content.StartsWith("[", StringComparison.Ordinal))
        {
            // "[Running] tool\n..." or "[Completed] tool\n..."
            var rb = chunk.Content.IndexOf(']');
            if (rb > 0)
            {
                var stateStr = chunk.Content.Substring(1, rb - 1);
                if (Enum.TryParse<ToolState>(stateStr, out var parsed)) state = parsed;
                var rest = chunk.Content[(rb + 1)..].TrimStart('\n', ' ');
                var nl = rest.IndexOf('\n');
                if (nl > 0)
                {
                    if (string.IsNullOrEmpty(name)) name = rest[..nl].Trim();
                    output = rest[(nl + 1)..];
                }
                else
                {
                    if (string.IsNullOrEmpty(name)) name = rest.Trim();
                }
            }
        }
        return (string.IsNullOrEmpty(name) ? "tool" : name, state, output);
    }

    private static string ExtractToolName(string content)
    {
        var nl = content.IndexOf('\n');
        return (nl > 0 ? content[..nl] : content).Trim();
    }

    private static string ExtractToolNameFromChunk(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "tool";
        }

        if (content.StartsWith("[", StringComparison.Ordinal))
        {
            var rb = content.IndexOf(']');
            if (rb > 0)
            {
                var rest = content[(rb + 1)..].TrimStart('\n', ' ');
                var nl = rest.IndexOf('\n');
                var name = nl > 0 ? rest[..nl] : rest;
                return string.IsNullOrWhiteSpace(name) ? "tool" : name.Trim();
            }
        }

        var fallback = ExtractToolName(content);
        return string.IsNullOrWhiteSpace(fallback) ? "tool" : fallback;
    }

    private static ChatMessageViewModel MapRemoteMessage(RemoteMessage msg)
    {
        var author = msg.Role == ChatRole.User ? "你" : "Codex";
        var vm = new ChatMessageViewModel(msg.Id, msg.Role, author);
        foreach (var b in msg.Blocks)
        {
            switch (b.Kind)
            {
                case ChatBlockKind.Text:
                    vm.Blocks.Add(new ChatBlockViewModel(ChatBlockKind.Text, b.Text));
                    break;
                case ChatBlockKind.Thought:
                    vm.Blocks.Add(new ChatBlockViewModel(ChatBlockKind.Thought, b.Text, isExpanded: false));
                    break;
                case ChatBlockKind.Image:
                    vm.Blocks.Add(new ChatBlockViewModel(ChatBlockKind.Image, b.Text));
                    break;
                case ChatBlockKind.Tool:
                    vm.Blocks.Add(new ChatBlockViewModel(
                        b.ToolName ?? "tool",
                        b.ToolState ?? ToolState.Completed,
                        b.ToolOutput,
                        isExpanded: false));
                    break;
            }
        }
        return vm;
    }

    // ── Attachment commands (kept for XAML compat) ──────────────────────

    [RelayCommand]
    private void AddAttachment()
    {
        var index = Attachments.Count + 1;
        Attachments.Add(new ChatAttachment($"file-{index}.md", $"/mock/file-{index}.md", false));
    }

    [RelayCommand]
    private void AddImage()
    {
        var index = Attachments.Count + 1;
        Attachments.Add(new ChatAttachment($"image-{index}.png", $"/mock/file-{index}.png", true));
    }

    public void AddPastedImage(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        var displayName = Path.GetFileName(path);
        Attachments.Add(new ChatAttachment(displayName, path, isImage: true));
    }

    [RelayCommand]
    private void OpenPermissionMenu() { }

    [RelayCommand]
    private void OpenModelConfig() { }

    [RelayCommand]
    private void RemoveAttachment(ChatAttachment attachment) => Attachments.Remove(attachment);
}

/// <summary>
/// Lightweight static slot for the "current working directory" chosen by
/// the sidebar. The SidebarVM updates this when the user picks a project,
/// and the ChatVM reads from it on first message send.
/// </summary>
public static class ProjectsTracker
{
    private static string? _current;

    public static string? CurrentWorkingDirectory
    {
        get => _current;
        set => _current = value;
    }
}
