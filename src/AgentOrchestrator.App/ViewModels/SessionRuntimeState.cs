using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Threading;
using AgentOrchestrator.App.Models.Sidebar;
using AgentOrchestrator.App.Models.Chat;

namespace AgentOrchestrator.App.ViewModels;

internal sealed class SessionRuntimeState
{
    public string? SessionId { get; set; }
    public string? AgentSessionId { get; set; }
    public SessionRecord? Record { get; set; }
    public string HeaderTitle { get; set; } = "新对话";
    public string? CurrentWorkingDirectory { get; set; }
    public long? LastActivityAt { get; set; }
    public long? ViewedAt { get; set; }
    public string DraftText { get; set; } = string.Empty;
    public string SelectedPermissionKey { get; set; } = "full";
    public string SelectedModel { get; set; } = "codex";
    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];
    public ObservableCollection<ChatAttachment> Attachments { get; } = [];
    public ObservableCollection<SubagentActivityViewModel> SubagentActivities { get; } = [];
    public ObservableCollection<QueuedChatDraftViewModel> QueuedDrafts { get; } = [];
    public Queue<QueuedSendRequest> PendingSendQueue { get; } = new();
    public PendingQuestion? PendingQuestion { get; set; }
    public string? PendingQuestionStatus { get; set; }
    public string? StatusMessage { get; set; }
    public bool IsStreaming { get; set; }
    public bool SendPipelineActive { get; set; }
    public CancellationTokenSource? SendCts { get; set; }
    public CancellationTokenSource? SubagentRefreshCts { get; set; }
    public CancellationTokenSource? PendingQuestionCts { get; set; }
    public bool HistoryLoaded { get; set; }
    public int HistoryWindowSize { get; set; }
    public bool HasOlderHistory { get; set; }
    public bool IsLoadingOlderHistory { get; set; }
}
