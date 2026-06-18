using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.Chat;
using AgentOrchestrator.App.Models.Sidebar;

namespace AgentOrchestrator.App.Services.Agent;

/// <summary>
/// Placeholder implementation of <see cref="IAgentGateway"/>. All methods
/// throw <see cref="NotImplementedException"/> except <see cref="SendMessageAsync"/>,
/// which returns an empty stream. Used while the real backend is not yet
/// wired up — the DI container registers this when no real gateway is present.
/// </summary>
public sealed class NotImplementedAgentGateway : IAgentGateway
{
    public string AgentKind => "not-implemented";

    public Task<string> CreateSessionAsync(
        SessionCreateRequest request,
        CancellationToken ct = default)
        => throw new NotImplementedException(
            "No IAgentGateway implementation has been registered. " +
            "Wire up a real gateway (e.g. OpenCodeAgentGateway) in App.axaml.cs.");

    public Task<IReadOnlyList<RemoteSessionInfo>> ListSessionsAsync(
        CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task DeleteSessionAsync(
        string agentSessionId,
        CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<IReadOnlyList<RemoteMessage>> GetMessagesAsync(
        string agentSessionId,
        CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<string> GetSessionTitleAsync(
        string agentSessionId,
        CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<IReadOnlyList<SubagentActivitySnapshot>> GetSubagentActivitiesAsync(
        string agentSessionId,
        CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<IReadOnlyList<AgentQuestionRequest>> GetPendingQuestionsAsync(
        string agentSessionId,
        CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task SubmitQuestionAnswerAsync(
        string agentSessionId,
        string requestId,
        IReadOnlyList<IReadOnlyList<string>> answers,
        CancellationToken ct = default)
        => throw new NotImplementedException();

    public async IAsyncEnumerable<ChatStreamChunk> SendMessageAsync(
        string agentSessionId,
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        yield break;
    }
}
