using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.Chat;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.Services.Agent;
using AgentOrchestrator.App.Services.TaskGraph;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentOrchestrator.App.ViewModels;

public sealed partial class TaskGraphNodeDetailViewModel : ViewModelBase, IDisposable
{
    private readonly IAgentGateway _agent;
    private readonly ITaskGraphRuntimeHub _runtimeHub;
    private TaskNode? _node;
    private string? _graphId;
    private string? _workingDirectory;

    public TaskGraphNodeDetailViewModel(
        IAgentGateway agent,
        ITaskGraphRuntimeHub runtimeHub)
    {
        _agent = agent;
        _runtimeHub = runtimeHub;
    }

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    [ObservableProperty]
    private string _title = "节点详情";

    [ObservableProperty]
    private string? _sessionId;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorText;

    public async Task InitializeAsync(string graphId, TaskNode node, string? workingDirectory, CancellationToken ct = default)
    {
        DisposeSubscriptions();

        _graphId = graphId;
        _node = node;
        _workingDirectory = workingDirectory;
        Title = node.Title;
        SessionId = node.AgentSessionId;
        StatusText = node.StatusText;
        ErrorText = node.LastError;
        node.PropertyChanged += OnNodePropertyChanged;
        _runtimeHub.ChunkReceived += OnChunkReceived;
        _runtimeHub.NodeChanged += OnNodeChanged;
        await ReloadAsync(ct).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ReloadAsync(CancellationToken ct = default)
    {
        Messages.Clear();
        if (string.IsNullOrWhiteSpace(SessionId))
        {
            return;
        }

        IsLoading = true;
        try
        {
            var remoteMessages = await _agent.GetMessagesAsync(SessionId, limit: null, ct: ct).ConfigureAwait(true);
            foreach (var message in remoteMessages)
            {
                Messages.Add(TaskGraphChatMapper.MapRemoteMessage(message, _workingDirectory));
            }
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnChunkReceived(object? sender, TaskGraphChunkEventArgs e)
    {
        if (_graphId is null || _node is null)
        {
            return;
        }

        if (!string.Equals(e.GraphId, _graphId, StringComparison.Ordinal)
            || !string.Equals(e.NodeId, _node.Id, StringComparison.Ordinal))
        {
            return;
        }

        var assistant = TaskGraphChatMapper.EnsureAssistantMessage(Messages, e.Chunk.MessageId);
        assistant.IsStreaming = true;
        TaskGraphChatMapper.ApplyChunk(assistant, e.Chunk, _workingDirectory);
    }

    private void OnNodeChanged(object? sender, TaskGraphNodeEventArgs e)
    {
        if (_graphId is null || _node is null)
        {
            return;
        }

        if (!string.Equals(e.GraphId, _graphId, StringComparison.Ordinal)
            || !string.Equals(e.NodeId, _node.Id, StringComparison.Ordinal))
        {
            return;
        }

        StatusText = _node.StatusText;
        ErrorText = _node.LastError;
        foreach (var message in Messages.Where(x => x.IsAssistant))
        {
            message.IsStreaming = _node.Status == TaskNodeStatus.Running;
        }
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_node is null)
        {
            return;
        }

        if (e.PropertyName is nameof(TaskNode.Status) or nameof(TaskNode.LastError) or nameof(TaskNode.AgentSessionId))
        {
            StatusText = _node.StatusText;
            ErrorText = _node.LastError;
            SessionId = _node.AgentSessionId;
        }
    }

    private void DisposeSubscriptions()
    {
        if (_node is not null)
        {
            _node.PropertyChanged -= OnNodePropertyChanged;
        }

        _runtimeHub.ChunkReceived -= OnChunkReceived;
        _runtimeHub.NodeChanged -= OnNodeChanged;
    }

    public void Dispose()
    {
        DisposeSubscriptions();
        GC.SuppressFinalize(this);
    }
}
