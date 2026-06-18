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
    private readonly Queue<QueuedSendRequest> _pendingSendQueue = new();
    private CancellationTokenSource? _sendCts;
    private CancellationTokenSource? _subagentRefreshCts;
    private CancellationTokenSource? _pendingQuestionCts;

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
        SubagentActivities.CollectionChanged += OnSubagentActivitiesChanged;
        QueuedDrafts.CollectionChanged += OnQueuedDraftsChanged;
    }

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];
    public ObservableCollection<ChatAttachment> Attachments { get; } = [];
    public ObservableCollection<SubagentActivityViewModel> SubagentActivities { get; } = [];
    public ObservableCollection<QueuedChatDraftViewModel> QueuedDrafts { get; } = [];
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

    [ObservableProperty]
    private PendingQuestion? _pendingQuestion;

    [ObservableProperty]
    private string? _pendingQuestionStatus;

    public bool HasAttachments => Attachments.Count > 0;
    public bool HasSubagentActivities => SubagentActivities.Count > 0;
    public bool HasQueuedDrafts => QueuedDrafts.Count > 0;
    public bool HasPendingQuestion => PendingQuestion is not null;
    public bool IsBlankPage => CurrentSessionId is null && Messages.Count == 0;
    public bool CanQueueCurrentDraft => !string.IsNullOrWhiteSpace(DraftText.Trim()) || Attachments.Count > 0;
    public bool ShowSendButton => !ShowStopButton;
    public bool ShowStopButton => IsStreaming && !CanQueueCurrentDraft;

    /// <summary>Fired when a brand-new session is created (so MainWindow can switch workspace).</summary>
    public event EventHandler? SessionChanged;

    partial void OnSelectedPermissionChanged(PermissionOption value)
    {
        foreach (var item in Permissions) item.IsSelected = ReferenceEquals(item, value);
    }

    private void OnAttachmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasAttachments));
        OnComposerStateChanged();
    }

    private void OnSubagentActivitiesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(HasSubagentActivities));

    private void OnQueuedDraftsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(HasQueuedDrafts));

    partial void OnDraftTextChanged(string value)
        => OnComposerStateChanged();

    partial void OnIsStreamingChanged(bool value)
        => OnComposerStateChanged();

    private void OnComposerStateChanged()
    {
        OnPropertyChanged(nameof(CanQueueCurrentDraft));
        OnPropertyChanged(nameof(ShowSendButton));
        OnPropertyChanged(nameof(ShowStopButton));
        OnPropertyChanged(nameof(HasPendingQuestion));
    }

    [RelayCommand]
    private void ClosePendingQuestion()
    {
        PendingQuestion = null;
        PendingQuestionStatus = null;
        OnPropertyChanged(nameof(HasPendingQuestion));
    }

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
        ClearPendingQueue();
        PendingQuestion = null;
        PendingQuestionStatus = null;
        await RefreshSubagentActivitiesAsync(record.AgentSessionId, ct).ConfigureAwait(true);
        await RefreshPendingQuestionAsync(record.AgentSessionId, ct).ConfigureAwait(true);
        StatusMessage = null;

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
        SubagentActivities.Clear();
        ClearPendingQueue();
        PendingQuestion = null;
        PendingQuestionStatus = null;
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
        var request = CaptureDraft();
        if (request is null) return;

        if (IsStreaming)
        {
            EnqueuePendingDraft(request);
            StatusMessage = $"已加入队列，前方还有 {QueuedDrafts.Count} 条";
            return;
        }

        await RunSendQueueAsync(request).ConfigureAwait(true);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task PrimaryActionAsync()
    {
        if (ShowStopButton)
        {
            Cancel();
            return;
        }

        await SendAsync().ConfigureAwait(true);
    }

    private async Task RunSendQueueAsync(QueuedSendRequest firstRequest)
    {
        var request = firstRequest;
        while (request is not null)
        {
            await SendOneAsync(request).ConfigureAwait(true);
            request = DequeuePendingDraft();
        }
    }

    private async Task SendOneAsync(QueuedSendRequest request)
    {
        // 1. Build the user message locally so the UI reflects it immediately.
        var userMessage = new ChatMessageViewModel(Guid.NewGuid().ToString("N"), ChatRole.User, "你");
        if (!string.IsNullOrWhiteSpace(request.Prompt))
        {
            userMessage.Blocks.Add(new ChatBlockViewModel(ChatBlockKind.Text, partId: null, text: request.Prompt));
        }
        foreach (var attachment in request.Attachments)
        {
            userMessage.Blocks.Add(new ChatBlockViewModel(ChatBlockKind.Image, partId: null, text: attachment.DisplayName, assetPath: attachment.Path));
        }
        Messages.Add(userMessage);

        // 2. Placeholder assistant message that streaming chunks will append to.
        var assistantId = Guid.NewGuid().ToString("N");
        var assistantMessage = new ChatMessageViewModel(assistantId, ChatRole.Assistant, "Codex")
        {
            IsStreaming = true,
            StreamingStatusText = "正在发送…",
        };
        Messages.Add(assistantMessage);
        IsStreaming = true;

        _sendCts = new CancellationTokenSource();
        var ct = _sendCts.Token;
        StartSubagentRefreshLoop(ct);
        StartPendingQuestionRefreshLoop(ct);

        try
        {
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

            var chatRequest = new AgentChatRequest(
                Prompt: request.Prompt,
                Attachments: request.Attachments,
                Permission: SelectedPermission.Key,
                Model: SelectedModel);

            await foreach (var chunk in _agent
                .SendMessageAsync(CurrentAgentSessionId!, chatRequest, ct)
                .ConfigureAwait(true))
            {
                assistantMessage.StreamingStatusText = null;
                ApplyChunk(assistantMessage, chunk);
            }

            await SyncFinalAssistantStateAsync(assistantMessage, ct).ConfigureAwait(true);
            await RefreshSubagentActivitiesAsync(CurrentAgentSessionId!, ct).ConfigureAwait(true);
            await TrySyncTitleAsync(ct).ConfigureAwait(true);
            assistantMessage.StreamingStatusText = null;
        }
        catch (OperationCanceledException)
        {
            assistantMessage.StreamingStatusText = null;
        }
        catch (Exception ex)
        {
            assistantMessage.StreamingStatusText = $"发送失败:{ex.Message}";
        }
        finally
        {
            assistantMessage.IsStreaming = false;
            IsStreaming = false;
            StopSubagentRefreshLoop();
            StopPendingQuestionRefreshLoop();
            SendCancellationCleanup();
        }
    }

    private QueuedSendRequest? CaptureDraft()
    {
        var prompt = DraftText.Trim();
        var snapshotAttachments = Attachments.ToList();
        if (string.IsNullOrWhiteSpace(prompt) && snapshotAttachments.Count == 0)
        {
            return null;
        }

        DraftText = string.Empty;
        Attachments.Clear();
        return new QueuedSendRequest(Guid.NewGuid().ToString("N"), prompt, snapshotAttachments);
    }

    private void EnqueuePendingDraft(QueuedSendRequest request)
    {
        _pendingSendQueue.Enqueue(request);
        QueuedDrafts.Add(new QueuedChatDraftViewModel(request.Id, request.Prompt, request.Attachments));
    }

    private QueuedSendRequest? DequeuePendingDraft()
    {
        if (_pendingSendQueue.Count == 0)
        {
            return null;
        }

        var request = _pendingSendQueue.Dequeue();
        var vm = QueuedDrafts.FirstOrDefault(x => x.Id == request.Id);
        if (vm is not null)
        {
            QueuedDrafts.Remove(vm);
        }
        return request;
    }

    private void ClearPendingQueue()
    {
        _pendingSendQueue.Clear();
        QueuedDrafts.Clear();
    }

    private void SendCancellationCleanup()
    {
        _sendCts?.Dispose();
        _sendCts = null;
    }

    private void StartSubagentRefreshLoop(CancellationToken ct)
    {
        StopSubagentRefreshLoop();
        _subagentRefreshCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = RefreshSubagentActivitiesLoopAsync(_subagentRefreshCts.Token);
    }

    private void StopSubagentRefreshLoop()
    {
        _subagentRefreshCts?.Cancel();
        _subagentRefreshCts?.Dispose();
        _subagentRefreshCts = null;
    }

    private void StartPendingQuestionRefreshLoop(CancellationToken ct)
    {
        StopPendingQuestionRefreshLoop();
        _pendingQuestionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = RefreshPendingQuestionLoopAsync(_pendingQuestionCts.Token);
    }

    private void StopPendingQuestionRefreshLoop()
    {
        _pendingQuestionCts?.Cancel();
        _pendingQuestionCts?.Dispose();
        _pendingQuestionCts = null;
    }

    private async Task RefreshSubagentActivitiesLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!IsStreaming || CurrentAgentSessionId is null)
                {
                    return;
                }

                await RefreshSubagentActivitiesAsync(CurrentAgentSessionId, ct).ConfigureAwait(true);
                await Task.Delay(TimeSpan.FromSeconds(1.5), ct).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(true);
            }
        }
    }

    private async Task RefreshPendingQuestionLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (CurrentAgentSessionId is null)
                {
                    PendingQuestion = null;
                    return;
                }

                await RefreshPendingQuestionAsync(CurrentAgentSessionId, ct).ConfigureAwait(true);
                await Task.Delay(TimeSpan.FromSeconds(0.8), ct).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                await Task.Delay(TimeSpan.FromSeconds(1.5), ct).ConfigureAwait(true);
            }
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _sendCts?.Cancel();
    }

    [RelayCommand]
    private void RemoveQueuedDraft(QueuedChatDraftViewModel draft)
    {
        if (draft is null)
        {
            return;
        }

        var remaining = _pendingSendQueue.Where(x => x.Id != draft.Id).ToArray();
        _pendingSendQueue.Clear();
        foreach (var item in remaining)
        {
            _pendingSendQueue.Enqueue(item);
        }

        QueuedDrafts.Remove(draft);
        OnPropertyChanged(nameof(HasQueuedDrafts));
    }

    [RelayCommand]
    private void RestoreQueuedDraft(QueuedChatDraftViewModel draft)
    {
        if (draft is null)
        {
            return;
        }

        RemoveQueuedDraft(draft);
        DraftText = draft.Prompt;
        Attachments.Clear();
        foreach (var attachment in draft.Attachments)
        {
            Attachments.Add(attachment);
        }
    }

    [RelayCommand]
    private void SelectPendingQuestionOption(PendingQuestionOption option)
    {
        if (PendingQuestion is null || option is null)
        {
            return;
        }

        if (!PendingQuestion.MultipleSelection)
        {
            foreach (var item in PendingQuestion.Questions)
            {
                foreach (var candidate in item.Options)
                {
                    candidate.IsSelected = ReferenceEquals(candidate, option);
                }
            }
            return;
        }

        option.IsSelected = !option.IsSelected;
    }

    [RelayCommand]
    private async Task SubmitPendingQuestionAsync()
    {
        if (PendingQuestion is null || CurrentAgentSessionId is null)
        {
            return;
        }

        var answers = BuildQuestionAnswers(PendingQuestion);
        if (answers.Count == 0)
        {
            PendingQuestionStatus = "请先选择答案";
            return;
        }

        try
        {
            PendingQuestionStatus = "正在提交…";
            await _agent.SubmitQuestionAnswerAsync(
                CurrentAgentSessionId,
                PendingQuestion.RequestId,
                answers,
                _sendCts?.Token ?? CancellationToken.None).ConfigureAwait(true);
            PendingQuestionStatus = null;
            PendingQuestion = null;
            await RefreshPendingQuestionAsync(CurrentAgentSessionId, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            PendingQuestionStatus = $"提交失败:{ex.Message}";
        }
    }

    public void RestorePendingQuestion(RemoteQuestion question)
    {
        PendingQuestion = MapPendingQuestion(question);
        PendingQuestionStatus = null;
        OnPropertyChanged(nameof(HasPendingQuestion));
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
        var block = FindBlockForChunk(assistant, chunk);
        if (block is null)
        {
            block = CreateBlockForChunk(chunk);
            if (block is null) return;
            assistant.Blocks.Add(block);
        }

        switch (chunk.Kind)
        {
            case ChatBlockKind.Text:
            case ChatBlockKind.Thought:
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    block.Text = (block.Text ?? string.Empty) + chunk.Content;
                }
                break;

            case ChatBlockKind.Image:
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    block.Text = chunk.Content;
                }
                break;

            case ChatBlockKind.Tool:
            case ChatBlockKind.Task:
                {
                    var (toolName, toolState, toolOutput) = ParseToolChunk(block, chunk);
                    block.ToolName = toolName;
                    block.ToolState = toolState;
                    block.ToolOutput = toolOutput;
                }
                break;
        }
    }

    private static ChatBlockViewModel? CreateBlockForChunk(ChatStreamChunk chunk)
    {
        return chunk.Kind switch
        {
            ChatBlockKind.Text => new ChatBlockViewModel(ChatBlockKind.Text, chunk.PartId, string.Empty),
            ChatBlockKind.Thought => new ChatBlockViewModel(ChatBlockKind.Thought, chunk.PartId, string.Empty, isExpanded: false),
            ChatBlockKind.Tool => new ChatBlockViewModel(ChatBlockKind.Tool, chunk.PartId, ExtractToolNameFromChunk(chunk.Content), ToolState.Running, string.Empty, isExpanded: false),
            ChatBlockKind.Task => new ChatBlockViewModel(ChatBlockKind.Task, chunk.PartId, ExtractToolNameFromChunk(chunk.Content), ToolState.Running, string.Empty, isExpanded: false),
            ChatBlockKind.Image => new ChatBlockViewModel(ChatBlockKind.Image, chunk.PartId, chunk.Content),
            _ => null,
        };
    }

    private static ChatBlockViewModel? FindBlockForChunk(ChatMessageViewModel assistant, ChatStreamChunk chunk)
    {
        if (!string.IsNullOrEmpty(chunk.PartId))
        {
            var matchByPart = assistant.Blocks.LastOrDefault(b => string.Equals(b.PartId, chunk.PartId, StringComparison.Ordinal));
            if (matchByPart is not null)
            {
                return matchByPart;
            }
        }

        var last = assistant.Blocks.LastOrDefault();
        if (last is null || last.Kind != chunk.Kind)
        {
            return null;
        }

        if (chunk.Kind is not (ChatBlockKind.Tool or ChatBlockKind.Task))
        {
            return last;
        }

        return string.Equals(
            last.ToolName ?? string.Empty,
            ExtractToolNameFromChunk(chunk.Content),
            StringComparison.Ordinal)
            ? last
            : null;
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
                    vm.Blocks.Add(new ChatBlockViewModel(ChatBlockKind.Text, b.PartId, b.Text));
                    break;
                case ChatBlockKind.Thought:
                    vm.Blocks.Add(new ChatBlockViewModel(ChatBlockKind.Thought, b.PartId, b.Text, isExpanded: false));
                    break;
                case ChatBlockKind.Image:
                    vm.Blocks.Add(new ChatBlockViewModel(ChatBlockKind.Image, b.PartId, b.Text));
                    break;
                case ChatBlockKind.Tool:
                {
                    var block = new ChatBlockViewModel(
                        ChatBlockKind.Tool,
                        b.PartId,
                        b.ToolName ?? "tool",
                        b.ToolState ?? ToolState.Completed,
                        b.ToolOutput,
                        isExpanded: false);
                    block.ToolQuestion = b.Question;
                    vm.Blocks.Add(block);
                    break;
                }
                case ChatBlockKind.Task:
                {
                    var block = new ChatBlockViewModel(
                        ChatBlockKind.Task,
                        b.PartId,
                        b.ToolName ?? "Task",
                        b.ToolState ?? ToolState.Completed,
                        b.ToolOutput,
                        isExpanded: false);
                    block.ToolQuestion = b.Question;
                    vm.Blocks.Add(block);
                    break;
                }
            }
        }
        return vm;
    }

    private async Task SyncFinalAssistantStateAsync(ChatMessageViewModel assistant, CancellationToken ct)
    {
        if (CurrentAgentSessionId is null)
        {
            return;
        }

        var messages = await _agent.GetMessagesAsync(CurrentAgentSessionId, ct).ConfigureAwait(true);
        var remoteAssistant = messages.LastOrDefault(m => m.Role == ChatRole.Assistant);
        if (remoteAssistant is null) return;

        var existing = assistant.Blocks
            .Where(b => !string.IsNullOrEmpty(b.PartId))
            .ToDictionary(b => b.PartId!, StringComparer.Ordinal);

        foreach (var block in remoteAssistant.Blocks)
        {
            if (!string.IsNullOrEmpty(block.PartId) && existing.TryGetValue(block.PartId, out var current))
            {
                current.Text = block.Text;
                current.ToolName = block.ToolName;
                current.ToolState = block.ToolState ?? current.ToolState;
                current.ToolOutput = block.ToolOutput;
                current.ToolQuestion = block.Question;
            }
            else
            {
                assistant.Blocks.Add(CreateRemoteBlock(block));
            }
        }
    }

    private static ChatBlockViewModel CreateRemoteBlock(RemoteBlock block) =>
        block.Kind switch
        {
            ChatBlockKind.Text => new ChatBlockViewModel(ChatBlockKind.Text, block.PartId, block.Text),
            ChatBlockKind.Thought => new ChatBlockViewModel(ChatBlockKind.Thought, block.PartId, block.Text, isExpanded: false),
            ChatBlockKind.Image => new ChatBlockViewModel(ChatBlockKind.Image, block.PartId, block.Text),
            ChatBlockKind.Tool => CreateQuestionAwareToolBlock(ChatBlockKind.Tool, block),
            ChatBlockKind.Task => CreateQuestionAwareToolBlock(ChatBlockKind.Task, block),
            _ => new ChatBlockViewModel(ChatBlockKind.Text, block.PartId, block.Text)
        };

    private static ChatBlockViewModel CreateQuestionAwareToolBlock(ChatBlockKind kind, RemoteBlock block)
    {
        var vm = new ChatBlockViewModel(
            kind,
            block.PartId,
            kind == ChatBlockKind.Task ? block.ToolName ?? "Task" : block.ToolName ?? "tool",
            block.ToolState ?? ToolState.Completed,
            block.ToolOutput,
            isExpanded: false);
        vm.ToolQuestion = block.Question;
        return vm;
    }

    private async Task RefreshSubagentActivitiesAsync(string agentSessionId, CancellationToken ct)
    {
        try
        {
            var snapshots = await _agent.GetSubagentActivitiesAsync(agentSessionId, ct).ConfigureAwait(true);
            SubagentActivities.Clear();
            foreach (var snapshot in snapshots)
            {
                var vm = new SubagentActivityViewModel();
                vm.UpdateFrom(snapshot);
                SubagentActivities.Add(vm);
            }
        }
        catch
        {
            // Best-effort only.
        }
    }

    private async Task RefreshPendingQuestionAsync(string agentSessionId, CancellationToken ct)
    {
        try
        {
            var requests = await _agent.GetPendingQuestionsAsync(agentSessionId, ct).ConfigureAwait(true);
            var request = requests.FirstOrDefault();
            if (request is not null)
            {
                PendingQuestion = MapPendingQuestion(request);
                OnPropertyChanged(nameof(HasPendingQuestion));
                return;
            }

            PendingQuestion = TryRestorePendingQuestionFromMessages();
            OnPropertyChanged(nameof(HasPendingQuestion));
        }
        catch
        {
            // Best-effort only.
        }
    }

    private static PendingQuestion MapPendingQuestion(AgentQuestionRequest request)
    {
        var vm = new PendingQuestion(request.RequestId, request.Title)
        {
            PromptText = request.Title,
            MultipleSelection = request.Questions.Any(x => x.Multiple),
            AllowCustomAnswer = request.Questions.Any(x => x.Custom),
        };

        foreach (var question in request.Questions)
        {
            var item = new PendingQuestionItem(question.Id, question.Header, question.Question);
            foreach (var option in question.Options)
            {
                item.Options.Add(new PendingQuestionOption(
                    option.Label,
                    option.Description,
                    string.IsNullOrWhiteSpace(option.Value) ? option.Label : option.Value));
            }
            vm.Questions.Add(item);
        }

        return vm;
    }

    private PendingQuestion? TryRestorePendingQuestionFromMessages()
    {
        var block = Messages
            .Where(m => m.IsAssistant)
            .SelectMany(m => m.Blocks)
            .LastOrDefault(b => b.ToolQuestion is not null && b.ToolState is not ToolState.Completed and not ToolState.Failed);

        return block?.ToolQuestion is null ? null : MapPendingQuestion(block.ToolQuestion);
    }

    private static PendingQuestion MapPendingQuestion(RemoteQuestion question)
    {
        var vm = new PendingQuestion(question.RequestId, question.Title)
        {
            PromptText = question.Title,
            MultipleSelection = question.Questions.Any(x => x.Multiple),
            AllowCustomAnswer = question.Questions.Any(x => x.Custom),
        };

        foreach (var item in question.Questions)
        {
            var questionItem = new PendingQuestionItem(item.Id, item.Header, item.Question);
            foreach (var option in item.Options)
            {
                questionItem.Options.Add(new PendingQuestionOption(option.Label, option.Description, option.Value));
            }
            vm.Questions.Add(questionItem);
        }

        return vm;
    }

    private static IReadOnlyList<IReadOnlyList<string>> BuildQuestionAnswers(PendingQuestion question)
    {
        var result = new List<IReadOnlyList<string>>(question.Questions.Count);
        foreach (var item in question.Questions)
        {
            var selected = item.Options
                .Where(x => x.IsSelected)
                .Select(x => x.Value ?? x.Label)
                .ToList();

            if (selected.Count == 0 && question.AllowCustomAnswer && !string.IsNullOrWhiteSpace(question.CustomAnswer))
            {
                selected.Add(question.CustomAnswer.Trim());
            }

            if (selected.Count == 0)
            {
                return [];
            }

            result.Add(selected);
        }

        return result;
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

    private sealed record QueuedSendRequest(
        string Id,
        string Prompt,
        IReadOnlyList<ChatAttachment> Attachments);
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
