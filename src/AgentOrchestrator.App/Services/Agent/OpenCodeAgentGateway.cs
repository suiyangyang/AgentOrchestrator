using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.Chat;
using AgentOrchestrator.App.Models.Sidebar;
using OpenCode.Client;
using OpenCode.Client.Models;
using OpenCode.Client.Models.Events;
using OpenCode.Client.Requests;
using ChatToolState = AgentOrchestrator.App.Models.Chat.ToolState;
using OcSessionCreateRequest = OpenCode.Client.Requests.SessionCreateRequest;
using OcSessionPromptRequest = OpenCode.Client.Requests.SessionPromptRequest;
using OcPartInputRequest = OpenCode.Client.Requests.PartInputRequest;

namespace AgentOrchestrator.App.Services.Agent;

/// <summary>
/// <see cref="IAgentGateway"/> backed by the OpenCode HTTP server. Wraps
/// the typed <see cref="OpenCodeClient"/> and translates Agent-specific
/// DTOs (Part/Message/ToolState) into the chat-block vocabulary used by
/// the rest of the App.
///
/// Streaming: we subscribe to <c>/event</c> SSE, fire the prompt with
/// <c>/prompt_async</c>, then route the resulting events into chat chunks
/// until <c>session.idle</c> arrives.
/// </summary>
public sealed class OpenCodeAgentGateway : IAgentGateway, IAsyncDisposable
{
    private readonly OpenCodeClient _client;
    private readonly bool _ownsClient;

    public OpenCodeAgentGateway(OpenCodeClient client, bool ownsClient = false)
    {
        _client = client;
        _ownsClient = ownsClient;
    }

    /// <summary>Build a gateway from raw connection options.</summary>
    public static OpenCodeAgentGateway FromOptions(OpenCodeClientOptions options, bool ownsClient = true)
    {
        var client = new OpenCodeClient(options);
        return new OpenCodeAgentGateway(client, ownsClient);
    }

    public string AgentKind => "opencode";

    public async Task<string> CreateSessionAsync(
        SessionCreateRequest request,
        CancellationToken ct = default)
    {
        var body = new OcSessionCreateRequest
        {
            Title = request.Title,
        };
        var created = await _client.Sessions
            .CreateAsync(body, directory: request.WorkingDirectory, ct: ct)
            .ConfigureAwait(false);
        return created.Id;
    }

    public async Task<IReadOnlyList<RemoteSessionInfo>> ListSessionsAsync(
        CancellationToken ct = default)
    {
        var list = await _client.Sessions.ListAsync(directory: null, ct: ct).ConfigureAwait(false);
        var result = new List<RemoteSessionInfo>(list.Count);
        foreach (var s in list)
        {
            result.Add(new RemoteSessionInfo(
                AgentSessionId: s.Id,
                Title: s.Title,
                CreatedAt: s.Time.Created,
                UpdatedAt: s.Time.Updated));
        }
        return result;
    }

    public async Task DeleteSessionAsync(
        string agentSessionId,
        CancellationToken ct = default)
    {
        try
        {
            await _client.Sessions.DeleteAsync(agentSessionId, directory: null, ct: ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // Idempotent: missing session is not an error.
        }
    }

    public async Task<string> GetSessionTitleAsync(
        string agentSessionId,
        CancellationToken ct = default)
    {
        var s = await _client.Sessions
            .GetAsync(agentSessionId, directory: null, ct: ct)
            .ConfigureAwait(false);
        return s.Title ?? string.Empty;
    }

    public async Task<IReadOnlyList<RemoteMessage>> GetMessagesAsync(
        string agentSessionId,
        CancellationToken ct = default)
    {
        var messages = await _client.Sessions
            .MessagesAsync(agentSessionId, directory: null, ct: ct)
            .ConfigureAwait(false);

        var result = new List<RemoteMessage>(messages.Count);
        foreach (var mp in messages)
        {
            var role = ResolveRole(mp.Info);
            if (role is null) continue;

            var blocks = new List<RemoteBlock>();
            foreach (var p in mp.Parts)
            {
                if (TryConvertPart(p, out var block))
                {
                    blocks.Add(block);
                }
            }

            result.Add(new RemoteMessage(
                Id: ExtractMessageId(mp.Info),
                Role: role.Value,
                Blocks: blocks));
        }
        return result;
    }

    public async IAsyncEnumerable<ChatStreamChunk> SendMessageAsync(
        string agentSessionId,
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 1. Resolve session (need directory for SSE filter).
        var session = await _client.Sessions
            .GetAsync(agentSessionId, directory: null, ct: ct)
            .ConfigureAwait(false);

        // 2. Build prompt body.
        var prompt = new OcSessionPromptRequest
        {
            Parts = BuildPromptParts(request),
        };

        // 3. Subscribe to SSE BEFORE firing the prompt so we don't miss early deltas.
        var eventStream = await _client.Events
            .SubscribeAsync(directory: session.Directory, ct: ct)
            .ConfigureAwait(false);

        // 4. Fire the prompt (fire-and-forget; events will carry the response).
        await _client.Sessions
            .PromptAsyncAsync(agentSessionId, prompt, directory: session.Directory, ct: ct)
            .ConfigureAwait(false);

        // 5. Translate events into chat chunks. End the stream on session.idle.
        var idle = false;
        var pending = new List<ChatStreamChunk>(capacity: 8);

        await foreach (var ev in eventStream.WithCancellation(ct).ConfigureAwait(false))
        {
            switch (ev.Payload)
            {
                case EventMessagePartUpdated p:
                {
                    var sessionId = GetPartSessionId(p.Properties.Part);
                    var messageId = GetPartMessageId(p.Properties.Part);
                    if (sessionId is null || messageId is null) break;
                    if (!string.Equals(sessionId, agentSessionId, StringComparison.Ordinal))
                        break;
                    if (TryConvertPart(p.Properties.Part, out var block))
                    {
                        var delta = p.Properties.Delta;
                        var payload = !string.IsNullOrEmpty(delta)
                            ? delta
                            : (block.Kind switch
                            {
                                ChatBlockKind.Text => block.Text ?? string.Empty,
                                ChatBlockKind.Thought => block.Text ?? string.Empty,
                                ChatBlockKind.Tool => BuildToolContent(block),
                                _ => string.Empty,
                            });
                        pending.Add(new ChatStreamChunk(
                            MessageId: messageId,
                            Kind: block.Kind,
                            Content: payload,
                            Complete: false));
                    }
                    break;
                }
                case EventMessageUpdated mu:
                {
                    if (mu.Properties.Info is AssistantMessageWrapper aw)
                    {
                        if (!string.Equals(aw.Value.SessionID, agentSessionId, StringComparison.Ordinal))
                            break;
                        pending.Add(new ChatStreamChunk(
                            MessageId: aw.Value.Id,
                            Kind: ChatBlockKind.Text,
                            Content: string.Empty,
                            Complete: true));
                    }
                    break;
                }
                case EventSessionIdle si:
                {
                    if (string.Equals(si.Properties.SessionID, agentSessionId, StringComparison.Ordinal))
                    {
                        idle = true;
                    }
                    break;
                }
            }

            foreach (var c in pending)
            {
                yield return c;
            }
            pending.Clear();

            if (idle)
            {
                yield break;
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string? GetPartSessionId(Part p) => p switch
    {
        TextPart tp => tp.SessionID,
        ReasoningPart rp => rp.SessionID,
        FilePart fp => fp.SessionID,
        ToolPart tp => tp.SessionID,
        StepStartPart sp => sp.SessionID,
        StepFinishPart sp => sp.SessionID,
        SnapshotPart sp => sp.SessionID,
        PatchPart pp => pp.SessionID,
        AgentPart ap => ap.SessionID,
        RetryPart rp => rp.SessionID,
        CompactionPart cp => cp.SessionID,
        SubtaskPart sp => sp.SessionID,
        _ => null,
    };

    private static string? GetPartMessageId(Part p) => p switch
    {
        TextPart tp => tp.MessageID,
        ReasoningPart rp => rp.MessageID,
        FilePart fp => fp.MessageID,
        ToolPart tp => tp.MessageID,
        StepStartPart sp => sp.MessageID,
        StepFinishPart sp => sp.MessageID,
        SnapshotPart sp => sp.MessageID,
        PatchPart pp => pp.MessageID,
        AgentPart ap => ap.MessageID,
        RetryPart rp => rp.MessageID,
        CompactionPart cp => cp.MessageID,
        SubtaskPart sp => sp.MessageID,
        _ => null,
    };

    private static string ExtractMessageId(Message message) => message switch
    {
        UserMessageWrapper u => u.Value.Id,
        AssistantMessageWrapper a => a.Value.Id,
        _ => Guid.NewGuid().ToString("N"),
    };

    private static ChatRole? ResolveRole(Message message) => message switch
    {
        UserMessageWrapper => ChatRole.User,
        AssistantMessageWrapper => ChatRole.Assistant,
        _ => null,
    };

    private static bool TryConvertPart(Part part, out RemoteBlock block)
    {
        switch (part)
        {
            case TextPart tp:
                block = new RemoteBlock(ChatBlockKind.Text, Text: tp.Text ?? string.Empty);
                return true;

            case ReasoningPart rp:
                block = new RemoteBlock(ChatBlockKind.Thought, Text: rp.Text ?? string.Empty);
                return true;

            case FilePart fp:
                block = new RemoteBlock(ChatBlockKind.Image, Text: fp.Filename ?? fp.Url);
                return true;

            case ToolPart tool:
            {
                var resolved = ResolveTool(tool);
                block = new RemoteBlock(
                    ChatBlockKind.Tool,
                    Text: null,
                    ToolName: resolved.Name,
                    ToolState: resolved.State,
                    ToolOutput: resolved.Output);
                return true;
            }

            // step-start / step-finish / snapshot / patch / agent / retry /
            // compaction / subtask → not user-visible, skip.
            default:
                block = default!;
                return false;
        }
    }

    private static (string Name, ChatToolState State, string? Output) ResolveTool(ToolPart tool)
    {
        var state = tool.State switch
        {
            ToolStatePending => ChatToolState.Pending,
            ToolStateRunning => ChatToolState.Running,
            ToolStateCompleted => ChatToolState.Completed,
            ToolStateError => ChatToolState.Failed,
            _ => ChatToolState.Pending,
        };
        string? output = null;
        if (tool.State is ToolStateCompleted c)
        {
            output = c.Output;
        }
        else if (tool.State is ToolStateError e)
        {
            output = e.Error;
        }
        else if (tool.State is ToolStateRunning r)
        {
            output = r.Title;
        }
        return (tool.Tool, state, output);
    }

    private static string BuildToolContent(RemoteBlock block)
    {
        var name = block.ToolName ?? "tool";
        var state = block.ToolState?.ToString() ?? "Pending";
        var output = block.ToolOutput ?? string.Empty;
        return $"[{state}] {name}\n{output}";
    }

    private static IReadOnlyList<OcPartInputRequest> BuildPromptParts(ChatRequest request)
    {
        var parts = new List<OcPartInputRequest>
        {
            new()
            {
                Type = "text",
                Text = request.Prompt ?? string.Empty,
            }
        };
        foreach (var att in request.Attachments ?? Array.Empty<ChatAttachment>())
        {
            if (att.IsImage)
            {
                parts.Add(new OcPartInputRequest
                {
                    Type = "file",
                    Mime = "image/*",
                    Filename = att.DisplayName,
                    Url = att.Path,
                });
            }
        }
        return parts;
    }

    public async ValueTask DisposeAsync()
    {
        if (_ownsClient)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }
    }
}
