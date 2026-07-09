using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.Chat;
using AgentOrchestrator.App.Models.Sidebar;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.Services.Agent;
using AgentOrchestrator.App.Services.TaskGraph;
using Xunit;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;
using AgentChatRequest = AgentOrchestrator.App.Services.Agent.ChatRequest;

namespace AgentOrchestrator.App.Tests;

public sealed class TaskGraphExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_PublishesTerminalExecutionState()
    {
        var agent = new FakeAgentGateway();
        agent.SeedSession("session-1", []);
        agent.MessagesAfterSend["session-1"] =
        [
            AssistantMessage("done")
        ];

        var runtimeHub = new TaskGraphRuntimeHub();
        var store = new InMemoryTaskGraphStore();
        var injector = new DefaultNodeOutputInjector();
        var executor = new TaskGraphExecutor(agent, store, injector, runtimeHub);

        TaskGraphExecutionState? terminalState = null;
        runtimeHub.ExecutionStateChanged += (_, args) =>
        {
            if (args.State is TaskGraphExecutionState.Completed
                or TaskGraphExecutionState.Failed
                or TaskGraphExecutionState.Cancelled)
            {
                terminalState = args.State;
            }
        };

        var graph = new TaskGraphModel { Name = "terminal-state" };
        graph.Nodes.Add(new TaskNode
        {
            Id = "n1",
            Title = "执行",
            Kind = TaskNodeKind.Execute,
            Prompt = "do work",
            DelegationStrategy = TaskNodeDelegationStrategy.NewSession,
        });

        await executor.ExecuteAsync(graph, Request());

        Assert.Equal(TaskGraphExecutionState.Completed, graph.ExecutionState);
        Assert.Equal(TaskGraphExecutionState.Completed, terminalState);
    }

    [Fact]
    public async Task ExecuteAsync_InSessionExecution_OnlyUsesNewMessagesForNodeOutput()
    {
        var agent = new FakeAgentGateway();
        var sessionId = "conversation-1";
        agent.SeedSession(sessionId,
        [
            AssistantMessage("old assistant summary"),
            UserMessage("old user turn")
        ]);
        agent.MessagesAfterSend[sessionId] =
        [
            AssistantMessage("fresh node result")
        ];

        var runtimeHub = new TaskGraphRuntimeHub();
        var store = new InMemoryTaskGraphStore();
        var injector = new DefaultNodeOutputInjector();
        var executor = new TaskGraphExecutor(agent, store, injector, runtimeHub);

        var graph = new TaskGraphModel
        {
            Name = "in-session",
            ConversationSessionId = sessionId,
        };
        var node = new TaskNode
        {
            Id = "n1",
            Title = "当前会话执行",
            Kind = TaskNodeKind.Execute,
            Prompt = "do current-session work",
            DelegationStrategy = TaskNodeDelegationStrategy.InSessionExecution,
        };
        graph.Nodes.Add(node);

        await executor.ExecuteAsync(graph, Request(conversationSessionId: sessionId));

        Assert.Equal("fresh node result", node.RawOutput);
        Assert.Equal("fresh node result", node.OutputSummary);
        Assert.DoesNotContain("old assistant summary", node.RawOutput);
    }

    [Fact]
    public async Task ExecuteAsync_InlineExecution_UsesAgentInsteadOfPromptStub()
    {
        var agent = new FakeAgentGateway();
        var sessionId = "conversation-inline";
        agent.SeedSession(sessionId, []);
        agent.MessagesAfterSend[sessionId] =
        [
            AssistantMessage("inline model output")
        ];

        var runtimeHub = new TaskGraphRuntimeHub();
        var store = new InMemoryTaskGraphStore();
        var injector = new DefaultNodeOutputInjector();
        var executor = new TaskGraphExecutor(agent, store, injector, runtimeHub);

        var graph = new TaskGraphModel
        {
            Name = "inline",
            ConversationSessionId = sessionId,
        };
        var node = new TaskNode
        {
            Id = "n1",
            Title = "inline",
            Kind = TaskNodeKind.Plan,
            Prompt = "summarize this",
            DelegationStrategy = TaskNodeDelegationStrategy.Inline,
        };
        graph.Nodes.Add(node);

        await executor.ExecuteAsync(
            graph,
            Request(conversationSessionId: sessionId, allowInlineExecution: true));

        Assert.Equal("inline model output", node.RawOutput);
        Assert.Equal("inline model output", node.OutputSummary);
        Assert.NotEqual("[inline] summarize this", node.OutputSummary);
        Assert.Contains(agent.SentRequests, x => x.sessionId == sessionId);
    }

    private static TaskGraphExecutionRequest Request(
        string? conversationSessionId = null,
        bool allowInlineExecution = false)
        => new(
            WorkingDirectory: "E:\\Work\\Code\\Tools\\AgentOrchestrator",
            Permission: "workspace-write",
            Model: "test-model",
            ConversationSessionId: conversationSessionId,
            AllowInlineExecution: allowInlineExecution,
            PresentationMode: TaskGraphExecutionPresentationMode.ChatEmbedded);

    private static RemoteMessage AssistantMessage(string text) =>
        new(
            Id: GuidLike(),
            Role: ChatRole.Assistant,
            Blocks:
            [
                new RemoteBlock(ChatBlockKind.Text, Text: text)
            ]);

    private static RemoteMessage UserMessage(string text) =>
        new(
            Id: GuidLike(),
            Role: ChatRole.User,
            Blocks:
            [
                new RemoteBlock(ChatBlockKind.Text, Text: text)
            ]);

    private static string GuidLike() => System.Guid.NewGuid().ToString("N");

    private sealed class InMemoryTaskGraphStore : ITaskGraphStore
    {
        public readonly Dictionary<string, TaskGraphModel> Saved = new();

        public Task<IReadOnlyList<TaskGraphListItem>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TaskGraphListItem>>([]);

        public Task<TaskGraphModel?> LoadAsync(string id, CancellationToken ct = default)
            => Task.FromResult(Saved.TryGetValue(id, out var graph) ? graph : null);

        public Task SaveAsync(TaskGraphModel graph, CancellationToken ct = default)
        {
            Saved[graph.Id] = graph;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id, CancellationToken ct = default)
        {
            Saved.Remove(id);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TaskGraphListItem>> ListTemplatesAsync(CancellationToken ct = default)
            => throw new System.NotImplementedException();

        public Task<IReadOnlyList<TaskGraphListItem>> ListRuntimeGraphsAsync(CancellationToken ct = default)
            => throw new System.NotImplementedException();

        public Task<TaskGraphModel?> LoadTemplateAsync(string id, CancellationToken ct = default)
            => throw new System.NotImplementedException();

        public Task<TaskGraphModel> InstantiateTemplateAsync(string templateId, TemplateInstantiationOptions options, CancellationToken ct = default)
            => throw new System.NotImplementedException();
    }

    private sealed class FakeAgentGateway : IAgentGateway
    {
        private readonly Dictionary<string, List<RemoteMessage>> _messagesBySession = new(StringComparer.Ordinal);
        private int _nextSession = 1;

        public string AgentKind => "fake";

        public event EventHandler<AgentErrorEventArgs>? AgentErrorOccurred;
        public event EventHandler<AgentTodosUpdatedEventArgs>? TodosUpdated;

        public Dictionary<string, IReadOnlyList<RemoteMessage>> MessagesAfterSend { get; } = new(StringComparer.Ordinal);

        public List<(string sessionId, AgentChatRequest request)> SentRequests { get; } = [];

        public void SeedSession(string sessionId, IReadOnlyList<RemoteMessage> messages)
            => _messagesBySession[sessionId] = messages.ToList();

        public void ReportAgentError(string operation, Exception ex)
            => AgentErrorOccurred?.Invoke(this, new AgentErrorEventArgs(operation, ex.Message, ex));

        public Task<string> CreateSessionAsync(SessionCreateRequest request, CancellationToken ct = default)
        {
            var sessionId = $"session-{_nextSession++}";
            _messagesBySession[sessionId] = [];
            return Task.FromResult(sessionId);
        }

        public Task<IReadOnlyList<RemoteSessionInfo>> ListSessionsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RemoteSessionInfo>>([]);

        public Task DeleteSessionAsync(string agentSessionId, CancellationToken ct = default)
        {
            _messagesBySession.Remove(agentSessionId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RemoteMessage>> GetMessagesAsync(string agentSessionId, int? limit = null, CancellationToken ct = default)
        {
            _messagesBySession.TryGetValue(agentSessionId, out var list);
            IReadOnlyList<RemoteMessage> result = list ?? [];
            return Task.FromResult(result);
        }

        public Task<string> GetSessionTitleAsync(string agentSessionId, CancellationToken ct = default)
            => Task.FromResult(agentSessionId);

        public Task<IReadOnlyList<SubagentActivitySnapshot>> GetSubagentActivitiesAsync(string agentSessionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SubagentActivitySnapshot>>([]);

        public Task<IReadOnlyList<AgentQuestionRequest>> GetPendingQuestionsAsync(string agentSessionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AgentQuestionRequest>>([]);

        public Task SubmitQuestionAnswerAsync(string agentSessionId, string requestId, IReadOnlyList<IReadOnlyList<string>> answers, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<AgentTodoSnapshot>> GetTodosAsync(string agentSessionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AgentTodoSnapshot>>([]);

        public Task<IReadOnlyList<AgentCommandDefinition>> ListCommandsAsync(string workingDirectory, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AgentCommandDefinition>>([]);

        public Task<AgentCommandExecutionResult> ExecuteCommandAsync(string agentSessionId, string commandName, string arguments, CancellationToken ct = default)
            => Task.FromResult(new AgentCommandExecutionResult("message-command"));

        public Task<AgentSessionSnapshot> ForkSessionAsync(string agentSessionId, string messageId, CancellationToken ct = default)
            => Task.FromResult(new AgentSessionSnapshot(
                $"{agentSessionId}-fork",
                "fork",
                agentSessionId,
                AppContext.BaseDirectory,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

        public Task<AgentSessionSnapshot> RevertSessionAsync(string agentSessionId, string messageId, string? partId = null, CancellationToken ct = default)
            => Task.FromResult(new AgentSessionSnapshot(
                agentSessionId,
                agentSessionId,
                null,
                AppContext.BaseDirectory,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

        public async IAsyncEnumerable<ChatStreamChunk> SendMessageAsync(string agentSessionId, AgentChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            SentRequests.Add((agentSessionId, request));

            _messagesBySession.TryGetValue(agentSessionId, out var existing);
            var buffer = existing?.ToList() ?? [];
            if (MessagesAfterSend.TryGetValue(agentSessionId, out var additions))
            {
                buffer.AddRange(additions);
            }

            _messagesBySession[agentSessionId] = buffer;
            await Task.CompletedTask;
            yield break;
        }

        public Task<string> CreateChildSessionAsync(string parentSessionId, SessionCreateRequest request, CancellationToken ct = default)
        {
            var sessionId = $"child-{_nextSession++}";
            _messagesBySession[sessionId] = [];
            return Task.FromResult(sessionId);
        }

        public Task<IReadOnlyList<RemoteSessionInfo>> ListChildSessionsAsync(string parentSessionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RemoteSessionInfo>>([]);
    }
}
