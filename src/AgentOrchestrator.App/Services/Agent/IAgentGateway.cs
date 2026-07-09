using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.Chat;
using AgentOrchestrator.App.Models.Sidebar;

namespace AgentOrchestrator.App.Services.Agent;

/// <summary>Event args for agent connectivity / HTTP errors surfaced to UI.</summary>
public sealed class AgentErrorEventArgs : EventArgs
{
    public string Operation { get; }
    public string UserMessage { get; }
    public Exception Exception { get; }

    public AgentErrorEventArgs(string operation, string userMessage, Exception exception)
    {
        Operation = operation;
        UserMessage = userMessage;
        Exception = exception;
    }
}

public sealed class AgentTodosUpdatedEventArgs : EventArgs
{
    public string AgentSessionId { get; }
    public IReadOnlyList<AgentTodoSnapshot> Todos { get; }

    public AgentTodosUpdatedEventArgs(string agentSessionId, IReadOnlyList<AgentTodoSnapshot> todos)
    {
        AgentSessionId = agentSessionId;
        Todos = todos;
    }
}

/// <summary>Request payload for sending a message to a known Agent session.</summary>
public sealed record ChatRequest(
    string Prompt,
    IReadOnlyList<ChatAttachment> Attachments,
    string Permission,
    string Model
);

/// <summary>
/// Request payload for creating a new Agent session. The
/// <see cref="WorkingDirectory"/> doubles as the Agent's working directory.
/// </summary>
public sealed record SessionCreateRequest(
    string WorkingDirectory,
    string? Title
);

public sealed record AgentTodoSnapshot(
    string Id,
    string Content,
    string Status,
    string Priority
);

public sealed record AgentCommandDefinition(
    string Name,
    string? Description,
    string? Agent,
    string? Model,
    string? Template,
    bool IsBuiltIn = false
);

public sealed record AgentCommandExecutionResult(
    string MessageId
);

public sealed record AgentSessionSnapshot(
    string AgentSessionId,
    string Title,
    string? ParentAgentSessionId,
    string WorkingDirectory,
    long CreatedAt,
    long UpdatedAt
);

public sealed record AgentConfirmationRequest(
    string Title,
    string Message
);

/// <summary>
/// Agent-agnostic gateway. The ViewModel layer depends only on this
/// interface; the concrete <c>OpenCodeAgentGateway</c> wraps the
/// OpenCode HTTP client. Future agents (Claude-CLI, etc.) plug in here.
/// </summary>
public interface IAgentGateway
{
    /// <summary>Identifier for the active backend (e.g. "opencode").</summary>
    string AgentKind { get; }

    /// <summary>Fired when a public method throws due to connectivity / HTTP error.</summary>
    event EventHandler<AgentErrorEventArgs>? AgentErrorOccurred;

    /// <summary>Fired when the backend pushes fresh todo state for a session.</summary>
    event EventHandler<AgentTodosUpdatedEventArgs>? TodosUpdated;

    /// <summary>Report an agent connectivity error from outside the gateway (ViewModel side-effect).</summary>
    void ReportAgentError(string operation, Exception ex);

    /// <summary>
    /// Creates a new Agent session rooted at <paramref name="request"/>.<see cref="SessionCreateRequest.WorkingDirectory"/>.
    /// Returns the Agent-side session id used by every subsequent call.
    /// </summary>
    Task<string> CreateSessionAsync(
        SessionCreateRequest request,
        CancellationToken ct = default);

    /// <summary>Lists the Agent's sessions. Used for startup reconciliation.</summary>
    Task<IReadOnlyList<RemoteSessionInfo>> ListSessionsAsync(
        CancellationToken ct = default);

    /// <summary>Deletes the Agent-side session. Idempotent: missing session is not an error.</summary>
    Task DeleteSessionAsync(
        string agentSessionId,
        CancellationToken ct = default);

    /// <summary>
    /// Loads the full message history for a session, in display order.
    /// Message content is rebuilt to the chat-block vocabulary so the
    /// ViewModel layer never has to know about Agent-specific DTOs.
    /// </summary>
    Task<IReadOnlyList<RemoteMessage>> GetMessagesAsync(
        string agentSessionId,
        int? limit = null,
        CancellationToken ct = default);

    /// <summary>Returns the Agent's current Title for a session.</summary>
    Task<string> GetSessionTitleAsync(
        string agentSessionId,
        CancellationToken ct = default);

    Task<IReadOnlyList<SubagentActivitySnapshot>> GetSubagentActivitiesAsync(
        string agentSessionId,
        CancellationToken ct = default);

    Task<IReadOnlyList<AgentQuestionRequest>> GetPendingQuestionsAsync(
        string agentSessionId,
        CancellationToken ct = default);

    Task SubmitQuestionAnswerAsync(
        string agentSessionId,
        string requestId,
        IReadOnlyList<IReadOnlyList<string>> answers,
        CancellationToken ct = default);

    Task<IReadOnlyList<AgentTodoSnapshot>> GetTodosAsync(
        string agentSessionId,
        CancellationToken ct = default);

    Task<IReadOnlyList<AgentCommandDefinition>> ListCommandsAsync(
        string workingDirectory,
        CancellationToken ct = default);

    Task<AgentCommandExecutionResult> ExecuteCommandAsync(
        string agentSessionId,
        string commandName,
        string arguments,
        CancellationToken ct = default);

    Task<AgentSessionSnapshot> ForkSessionAsync(
        string agentSessionId,
        string messageId,
        CancellationToken ct = default);

    Task<AgentSessionSnapshot> RevertSessionAsync(
        string agentSessionId,
        string messageId,
        string? partId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Sends a message to an existing Agent session and yields streaming
    /// chunks. The stream completes when the Agent's response ends.
    /// </summary>
    IAsyncEnumerable<ChatStreamChunk> SendMessageAsync(
        string agentSessionId,
        ChatRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a child session under the given parent. The child session is
    /// rooted at <paramref name="request"/>.<see cref="SessionCreateRequest.WorkingDirectory"/>
    /// and is associated with <paramref name="parentSessionId"/>.
    /// Returns the child Agent-side session id.
    /// </summary>
    Task<string> CreateChildSessionAsync(
        string parentSessionId,
        SessionCreateRequest request,
        CancellationToken ct = default);

    /// <summary>Lists the child sessions for the given parent.</summary>
    Task<IReadOnlyList<RemoteSessionInfo>> ListChildSessionsAsync(
        string parentSessionId,
        CancellationToken ct = default);
}
