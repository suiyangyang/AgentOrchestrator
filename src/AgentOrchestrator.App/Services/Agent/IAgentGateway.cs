using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.Chat;
using AgentOrchestrator.App.Models.Sidebar;

namespace AgentOrchestrator.App.Services.Agent;

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

/// <summary>
/// Agent-agnostic gateway. The ViewModel layer depends only on this
/// interface; the concrete <c>OpenCodeAgentGateway</c> wraps the
/// OpenCode HTTP client. Future agents (Claude-CLI, etc.) plug in here.
/// </summary>
public interface IAgentGateway
{
    /// <summary>Identifier for the active backend (e.g. "opencode").</summary>
    string AgentKind { get; }

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
        CancellationToken ct = default);

    /// <summary>Returns the Agent's current Title for a session.</summary>
    Task<string> GetSessionTitleAsync(
        string agentSessionId,
        CancellationToken ct = default);

    /// <summary>
    /// Sends a message to an existing Agent session and yields streaming
    /// chunks. The stream completes when the Agent's response ends.
    /// </summary>
    IAsyncEnumerable<ChatStreamChunk> SendMessageAsync(
        string agentSessionId,
        ChatRequest request,
        CancellationToken ct = default);
}
