using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.Chat;
using AgentOrchestrator.App.Models.Sidebar;
using OpenCode.Client;
using OpenCode.Client.Models;
using OpenCode.Client.Models.Events;
using OpenCode.Client.Requests;
using ChatToolState = AgentOrchestrator.App.Models.Chat.ToolState;
using OcToolState = OpenCode.Client.Models.ToolState;
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

    public async Task<IReadOnlyList<SubagentActivitySnapshot>> GetSubagentActivitiesAsync(
        string agentSessionId,
        CancellationToken ct = default)
    {
        var session = await _client.Sessions
            .GetAsync(agentSessionId, directory: null, ct: ct)
            .ConfigureAwait(false);

        var children = await _client.Sessions
            .ChildrenAsync(agentSessionId, directory: session.Directory, ct: ct)
            .ConfigureAwait(false);
        var statuses = await _client.Sessions
            .StatusAsync(directory: session.Directory, ct: ct)
            .ConfigureAwait(false);

        var result = new List<SubagentActivitySnapshot>(children.Count);
        foreach (var child in children.OrderByDescending(x => x.Time.Updated))
        {
            statuses.TryGetValue(child.Id, out var status);
            var details = await GetSubagentDetailsAsync(child.Id, session.Directory, child.Time.Created, child.Time.Updated, ct)
                .ConfigureAwait(false);
            result.Add(new SubagentActivitySnapshot(
                SessionId: child.Id,
                Title: string.IsNullOrWhiteSpace(child.Title) ? child.Id : child.Title,
                StatusText: ResolveSessionStatusText(status),
                AgentName: details.AgentName,
                ModelName: details.ModelName,
                Content: details.Content,
                IsBusy: status is SessionStatusBusy or SessionStatusRetry,
                DurationMs: details.DurationMs,
                UpdatedAt: child.Time.Updated));
        }

        return result;
    }

    public async Task<IReadOnlyList<AgentQuestionRequest>> GetPendingQuestionsAsync(
        string agentSessionId,
        CancellationToken ct = default)
    {
        var session = await _client.Sessions
            .GetAsync(agentSessionId, directory: null, ct: ct)
            .ConfigureAwait(false);
        var questions = await _client.Sessions
            .QuestionsAsync(agentSessionId, directory: session.Directory, ct: ct)
            .ConfigureAwait(false);

        var result = new List<AgentQuestionRequest>(questions.Count);
        foreach (var question in questions)
        {
            result.Add(new AgentQuestionRequest(
                question.Id,
                question.Title,
                question.Questions.Select(q => new AgentQuestionItem(
                    q.Id,
                    q.Header,
                    q.Question,
                    q.Multiple,
                    q.Custom,
                    q.Options.Select(o => new AgentQuestionOption(
                        o.Label,
                        o.Description,
                        o.Value?.ToString())).ToArray()
                )).ToArray()));
        }

        return result;
    }

    public async Task SubmitQuestionAnswerAsync(
        string agentSessionId,
        string requestId,
        IReadOnlyList<IReadOnlyList<string>> answers,
        CancellationToken ct = default)
    {
        var session = await _client.Sessions
            .GetAsync(agentSessionId, directory: null, ct: ct)
            .ConfigureAwait(false);

        await _client.Sessions
            .ReplyQuestionAsync(agentSessionId, requestId, new QuestionReplyRequest { Answers = answers }, directory: session.Directory, ct: ct)
            .ConfigureAwait(false);
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
                if (TryConvertPart(p, out var convertedBlocks))
                {
                    blocks.AddRange(convertedBlocks);
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

        // 4. Fire the prompt without blocking event consumption. Some servers
        // don't complete this request until the full answer finishes, so
        // awaiting it here would turn the entire UI into "fake streaming".
        var promptTask = _client.Sessions
            .PromptAsyncAsync(agentSessionId, prompt, directory: session.Directory, ct: ct);

        // 5. Translate events into chat chunks. End the stream on session.idle.
        var idle = false;
        var assistantMessageIds = new HashSet<string>(StringComparer.Ordinal);
        var userMessageIds = new HashSet<string>(StringComparer.Ordinal);
        var promptText = (request.Prompt ?? string.Empty).Trim();
        var textPartStates = new Dictionary<string, StreamedTextPartState>(StringComparer.Ordinal);
        var streamedParts = new Dictionary<string, Part>(StringComparer.Ordinal);

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
                    var partId = GetPartId(p.Properties.Part);
                    if (!string.IsNullOrEmpty(partId))
                    {
                        streamedParts[partId] = p.Properties.Part;
                    }
                    if (TryConvertPart(p.Properties.Part, out var blocks))
                    {
                        if (ShouldSuppressUserEcho(p.Properties.Part, promptText, userMessageIds.Contains(messageId), assistantMessageIds.Contains(messageId)))
                        {
                            break;
                        }

                        foreach (var chunk in ConvertPartToChunks(messageId, p.Properties.Part, blocks, p.Properties.Delta, textPartStates))
                        {
                            yield return chunk;
                        }
                    }
                    break;
                }
                case EventMessagePartDelta pd:
                {
                    if (!string.Equals(pd.Properties.SessionID, agentSessionId, StringComparison.Ordinal))
                        break;
                    if (!streamedParts.TryGetValue(pd.Properties.PartID, out var knownPart))
                        break;
                    if (ShouldSuppressUserEcho(knownPart, promptText, userMessageIds.Contains(pd.Properties.MessageID), assistantMessageIds.Contains(pd.Properties.MessageID)))
                        break;

                    foreach (var chunk in ConvertPartDeltaToChunks(pd.Properties.MessageID, knownPart, pd.Properties.Field, pd.Properties.Delta, textPartStates))
                    {
                        yield return chunk;
                    }
                    break;
                }
                case EventMessageUpdated mu:
                {
                    if (mu.Properties.Info is AssistantMessageWrapper aw)
                    {
                        if (!string.Equals(aw.Value.SessionID, agentSessionId, StringComparison.Ordinal))
                            break;
                        assistantMessageIds.Add(aw.Value.Id);
                    }
                    else if (mu.Properties.Info is UserMessageWrapper uw
                        && string.Equals(uw.Value.SessionID, agentSessionId, StringComparison.Ordinal))
                    {
                        userMessageIds.Add(uw.Value.Id);
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

            if (idle)
            {
                await promptTask.ConfigureAwait(false);
                yield break;
            }
        }

        await promptTask.ConfigureAwait(false);
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

    private static bool TryConvertPart(Part part, out IReadOnlyList<RemoteBlock> blocks)
    {
        switch (part)
        {
            case TextPart tp:
                blocks = SplitTextBlocks(tp.Id, tp.Text);
                return true;

            case ReasoningPart rp:
                blocks = [new RemoteBlock(ChatBlockKind.Thought, PartId: rp.Id, Text: rp.Text ?? string.Empty)];
                return true;

            case FilePart fp:
                blocks = [new RemoteBlock(ChatBlockKind.Image, PartId: fp.Id, Text: fp.Filename ?? fp.Url)];
                return true;

            case ToolPart tool:
            {
                var resolved = ResolveTool(tool);
                var kind = IsTaskTool(tool) ? ChatBlockKind.Task : ChatBlockKind.Tool;
                blocks =
                [
                    new RemoteBlock(
                    kind,
                    PartId: tool.Id,
                    Text: null,
                    ToolName: resolved.Name,
                    ToolState: resolved.State,
                    ToolOutput: resolved.Output,
                    Question: TryBuildRemoteQuestion(tool))
                ];
                return true;
            }

            // step-start / step-finish / snapshot / patch / agent / retry /
            // compaction / subtask → not user-visible, skip.
            default:
                blocks = Array.Empty<RemoteBlock>();
                return false;
        }
    }

    private static bool ShouldSuppressUserEcho(
        Part part,
        string promptText,
        bool knownUserMessage,
        bool knownAssistantMessage)
    {
        if (knownAssistantMessage)
        {
            return false;
        }

        if (knownUserMessage)
        {
            return true;
        }

        return part switch
        {
            TextPart tp => string.Equals((tp.Text ?? string.Empty).Trim(), promptText, StringComparison.Ordinal),
            _ => false,
        };
    }

    private static IReadOnlyList<ChatStreamChunk> ConvertPartToChunks(
        string messageId,
        Part originalPart,
        IReadOnlyList<RemoteBlock> blocks,
        string? delta,
        Dictionary<string, StreamedTextPartState> textPartStates)
    {
        if (blocks.Count == 0)
        {
            return Array.Empty<ChatStreamChunk>();
        }

        if (originalPart is TextPart textPart)
        {
            return ConvertTextPartToChunks(messageId, textPart, delta, textPartStates);
        }

        if (originalPart is ReasoningPart reasoningPart)
        {
            return ConvertReasoningPartToChunks(messageId, reasoningPart, delta);
        }

        if (originalPart is ToolPart)
        {
            return
            [
                new ChatStreamChunk(
                    MessageId: messageId,
                    PartId: blocks[0].PartId,
                    Kind: blocks[0].Kind,
                    Content: BuildToolContent(blocks[0]),
                    Complete: false)
            ];
        }

        if (!string.IsNullOrEmpty(delta))
        {
            var deltaBlocks = SplitTextBlocks(GetPartId(originalPart), delta);
            var chunks = new List<ChatStreamChunk>(deltaBlocks.Count);
            foreach (var block in deltaBlocks)
            {
                chunks.Add(new ChatStreamChunk(
                    MessageId: messageId,
                    PartId: block.PartId,
                    Kind: block.Kind,
                    Content: block.Text ?? string.Empty,
                    Complete: false));
            }
            return chunks;
        }

        var result = new List<ChatStreamChunk>(blocks.Count);
        foreach (var block in blocks)
        {
            var payload = block.Kind switch
            {
                ChatBlockKind.Text => block.Text ?? string.Empty,
                ChatBlockKind.Thought => block.Text ?? string.Empty,
                ChatBlockKind.Tool => BuildToolContent(block),
                _ => string.Empty,
            };
            result.Add(new ChatStreamChunk(
                MessageId: messageId,
                PartId: block.PartId,
                Kind: block.Kind,
                Content: payload,
                Complete: false));
        }
        return result;
    }

    private static IReadOnlyList<ChatStreamChunk> ConvertPartDeltaToChunks(
        string messageId,
        Part originalPart,
        string field,
        string delta,
        Dictionary<string, StreamedTextPartState> textPartStates)
    {
        if (string.IsNullOrEmpty(delta))
        {
            return Array.Empty<ChatStreamChunk>();
        }

        if (originalPart is TextPart textPart)
        {
            return string.Equals(field, "text", StringComparison.Ordinal)
                ? ConvertTextPartToChunks(messageId, textPart, delta, textPartStates)
                : Array.Empty<ChatStreamChunk>();
        }

        if (originalPart is ReasoningPart reasoningPart)
        {
            return string.Equals(field, "text", StringComparison.Ordinal)
                ? ConvertReasoningPartToChunks(messageId, reasoningPart, delta)
                : Array.Empty<ChatStreamChunk>();
        }

        if (originalPart is ToolPart toolPart)
        {
            return ConvertToolPartDeltaToChunks(messageId, toolPart, field, delta);
        }

        var partId = GetPartId(originalPart);
        if (string.IsNullOrEmpty(partId))
        {
            return Array.Empty<ChatStreamChunk>();
        }

        return
        [
            new ChatStreamChunk(
                MessageId: messageId,
                PartId: partId,
                Kind: ChatBlockKind.Text,
                Content: delta,
                Complete: false)
        ];
    }

    private static IReadOnlyList<ChatStreamChunk> ConvertToolPartDeltaToChunks(
        string messageId,
        ToolPart toolPart,
        string field,
        string delta)
    {
        var kind = IsTaskTool(toolPart) ? ChatBlockKind.Task : ChatBlockKind.Tool;
        var headerState = ResolveTool(toolPart);
        var content = field switch
        {
            "text" => $"[{headerState.State}] {headerState.Name}\n{delta}",
            "title" => $"[{headerState.State}] {delta}",
            "output" => $"[{headerState.State}] {headerState.Name}\n{delta}",
            "error" => $"[{ChatToolState.Failed}] {headerState.Name}\n{delta}",
            _ => string.Empty,
        };

        if (string.IsNullOrEmpty(content))
        {
            return Array.Empty<ChatStreamChunk>();
        }

        return
        [
            new ChatStreamChunk(
                MessageId: messageId,
                PartId: toolPart.Id,
                Kind: kind,
                Content: content,
                Complete: false)
        ];
    }

    private static IReadOnlyList<ChatStreamChunk> ConvertReasoningPartToChunks(
        string messageId,
        ReasoningPart reasoningPart,
        string? delta)
    {
        var content = string.IsNullOrEmpty(delta) ? reasoningPart.Text : delta;
        return
        [
            new ChatStreamChunk(
                MessageId: messageId,
                PartId: reasoningPart.Id,
                Kind: ChatBlockKind.Thought,
                Content: content ?? string.Empty,
                Complete: false)
        ];
    }

    private static IReadOnlyList<ChatStreamChunk> ConvertTextPartToChunks(
        string messageId,
        TextPart textPart,
        string? delta,
        Dictionary<string, StreamedTextPartState> textPartStates)
    {
        if (!textPartStates.TryGetValue(textPart.Id, out var state))
        {
            state = new StreamedTextPartState();
            textPartStates[textPart.Id] = state;
        }

        var snapshotText = textPart.Text ?? string.Empty;
        if (string.IsNullOrEmpty(delta))
        {
            var snapshotBlocks = SplitTextBlocks(textPart.Id, snapshotText);
            var chunks = BuildIncrementalChunksFromSnapshot(messageId, textPart.Id, state, snapshotBlocks);
            state.SourceText = snapshotText;
            return chunks;
        }

        var nextSourceText = BuildNextSourceText(state.SourceText, snapshotText, delta);
        var blocks = SplitTextBlocks(textPart.Id, nextSourceText);
        var result = BuildIncrementalChunksFromSnapshot(messageId, textPart.Id, state, blocks);
        state.SourceText = nextSourceText;
        return result;
    }

    private static IReadOnlyList<ChatStreamChunk> BuildIncrementalChunksFromSnapshot(
        string messageId,
        string sourcePartId,
        StreamedTextPartState state,
        IReadOnlyList<RemoteBlock> blocks)
    {
        var chunks = new List<ChatStreamChunk>(blocks.Count);
        foreach (var block in blocks)
        {
            var partId = block.PartId ?? sourcePartId;
            var content = block.Text ?? string.Empty;
            var isKnownBlock = state.EmittedTextBySegmentId.TryGetValue(partId, out var previous);

            if (isKnownBlock && content.Length >= previous!.Length && content.StartsWith(previous, StringComparison.Ordinal))
            {
                var appended = content[previous.Length..];
                if (appended.Length > 0)
                {
                    chunks.Add(new ChatStreamChunk(
                        MessageId: messageId,
                        PartId: partId,
                        Kind: block.Kind,
                        Content: appended,
                        Complete: false));
                }
            }
            else if (!isKnownBlock)
            {
                chunks.Add(new ChatStreamChunk(
                    MessageId: messageId,
                    PartId: partId,
                    Kind: block.Kind,
                    Content: content,
                    Complete: false));
            }

            state.EmittedTextBySegmentId[partId] = content;
        }

        return chunks;
    }

    private static string BuildNextSourceText(string previousSourceText, string snapshotText, string delta)
    {
        if (!string.IsNullOrEmpty(snapshotText))
        {
            if (!string.IsNullOrEmpty(previousSourceText)
                && snapshotText.Length >= previousSourceText.Length
                && snapshotText.StartsWith(previousSourceText, StringComparison.Ordinal))
            {
                return snapshotText;
            }

            if (snapshotText.Length >= delta.Length && snapshotText.EndsWith(delta, StringComparison.Ordinal))
            {
                return snapshotText;
            }
        }

        return previousSourceText + delta;
    }

    private static IReadOnlyList<ChatStreamChunk> ConvertBlocksToChunks(
        string messageId,
        IReadOnlyList<RemoteBlock> blocks)
    {
        var result = new List<ChatStreamChunk>(blocks.Count);
        foreach (var block in blocks)
        {
            result.Add(new ChatStreamChunk(
                MessageId: messageId,
                PartId: block.PartId,
                Kind: block.Kind,
                Content: block.Text ?? string.Empty,
                Complete: false));
        }

        return result;
    }

    private static string? GetPartId(Part part) => part switch
    {
        TextPart tp => tp.Id,
        ReasoningPart rp => rp.Id,
        FilePart fp => fp.Id,
        ToolPart tp => tp.Id,
        _ => null,
    };

    private static IReadOnlyList<RemoteBlock> SplitTextBlocks(string? partId, string? text)
    {
        var source = text ?? string.Empty;
        ReadOnlySpan<string> openTags = ["<think>", "<thinking>"];
        ReadOnlySpan<string> closeTags = ["</think>", "</thinking>"];
        var blocks = new List<RemoteBlock>();
        var cursor = 0;
        var segmentIndex = 0;
        var mode = ChatBlockKind.Text;

        while (cursor < source.Length)
        {
            var nextTag = FindNextTag(
                source,
                cursor,
                mode == ChatBlockKind.Text ? openTags : closeTags);
            if (nextTag.Index >= 0)
            {
                AddTextSegment(blocks, partId, mode, segmentIndex++, source[cursor..nextTag.Index], forceCreate: false);
                cursor = nextTag.Index + nextTag.Tag.Length;
                mode = mode == ChatBlockKind.Text ? ChatBlockKind.Thought : ChatBlockKind.Text;
                if (cursor >= source.Length)
                {
                    AddTextSegment(blocks, partId, mode, segmentIndex++, string.Empty, forceCreate: true);
                }
                continue;
            }

            var remainder = source[cursor..];
            var hiddenSuffixLength = GetTrailingPartialTagLength(
                remainder,
                mode == ChatBlockKind.Text ? openTags : closeTags);
            var visibleLength = remainder.Length - hiddenSuffixLength;
            AddTextSegment(
                blocks,
                partId,
                mode,
                segmentIndex++,
                visibleLength > 0 ? remainder[..visibleLength] : string.Empty,
                forceCreate: mode == ChatBlockKind.Thought);
            break;
        }

        if (blocks.Count == 0)
        {
            return [new RemoteBlock(ChatBlockKind.Text, PartId: BuildTextSegmentPartId(partId, 0, ChatBlockKind.Text), Text: string.Empty)];
        }

        return blocks;
    }

    private static void AddTextSegment(
        List<RemoteBlock> blocks,
        string? originalPartId,
        ChatBlockKind kind,
        int segmentIndex,
        string text,
        bool forceCreate)
    {
        if (!forceCreate && text.Length == 0)
        {
            return;
        }

        blocks.Add(new RemoteBlock(
            kind,
            PartId: BuildTextSegmentPartId(originalPartId, segmentIndex, kind),
            Text: text));
    }

    private static string? BuildTextSegmentPartId(string? originalPartId, int segmentIndex, ChatBlockKind kind)
    {
        if (string.IsNullOrEmpty(originalPartId))
        {
            return null;
        }

        return $"{originalPartId}::seg{segmentIndex}:{kind}";
    }

    private static (int Index, string Tag) FindNextTag(string text, int startIndex, ReadOnlySpan<string> tags)
    {
        var bestIndex = -1;
        string bestTag = string.Empty;

        foreach (var tag in tags)
        {
            var index = text.IndexOf(tag, startIndex, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            if (bestIndex < 0 || index < bestIndex)
            {
                bestIndex = index;
                bestTag = tag;
            }
        }

        return (bestIndex, bestTag);
    }

    private static int GetTrailingPartialTagLength(string text, ReadOnlySpan<string> fullTags)
    {
        var bestLength = 0;

        foreach (var fullTag in fullTags)
        {
            var maxLength = Math.Min(text.Length, fullTag.Length - 1);
            for (var length = maxLength; length > 0; length--)
            {
                if (length <= bestLength)
                {
                    break;
                }

                if (fullTag.StartsWith(text[^length..], StringComparison.OrdinalIgnoreCase))
                {
                    bestLength = length;
                    break;
                }
            }
        }

        return bestLength;
    }

    private static (string Name, ChatToolState State, string? Output) ResolveTool(ToolPart tool)
    {
        if (IsQuestionTool(tool))
        {
            return ("Question", MapToolState(tool.State), BuildQuestionSummary(tool));
        }

        if (tool.State is ToolStateUnknown unknown)
        {
            return (ResolveUnknownToolName(tool, unknown), ChatToolState.Running, ResolveUnknownToolOutput(tool, unknown));
        }

        if (IsTaskTool(tool))
        {
            return ResolveTaskTool(tool);
        }

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

    private static (string Name, ChatToolState State, string? Output) ResolveTaskTool(ToolPart tool)
    {
        var state = MapToolState(tool.State);
        var title = ResolveTaskTitle(tool);
        var output = ResolveTaskOutput(tool);
        return (title, state, output);
    }

    private static ChatToolState MapToolState(OcToolState state) => state switch
    {
        ToolStateUnknown => ChatToolState.Running,
        ToolStatePending => ChatToolState.Pending,
        ToolStateRunning => ChatToolState.Running,
        ToolStateCompleted => ChatToolState.Completed,
        ToolStateError => ChatToolState.Failed,
        _ => ChatToolState.Pending,
    };

    private static bool IsTaskTool(ToolPart tool)
        => string.Equals(tool.Tool, "task", StringComparison.OrdinalIgnoreCase);

    private static bool IsQuestionTool(ToolPart tool)
        => string.Equals(tool.Tool, "question", StringComparison.OrdinalIgnoreCase);

    private static string ResolveTaskTitle(ToolPart tool)
    {
        if (tool.State is ToolStateCompleted completed && !string.IsNullOrWhiteSpace(completed.Title))
        {
            return completed.Title.Trim();
        }

        if (tool.State is ToolStateRunning running && !string.IsNullOrWhiteSpace(running.Title))
        {
            return running.Title.Trim();
        }

        if (TryGetJsonString(tool.Metadata, "title", out var metadataTitle))
        {
            return metadataTitle;
        }

        if (TryGetJsonString(tool.Metadata, "prompt", out var prompt))
        {
            return prompt;
        }

        return "Task";
    }

    private static string? ResolveTaskOutput(ToolPart tool)
    {
        if (tool.State is ToolStateUnknown)
        {
            return null;
        }

        if (tool.State is ToolStateCompleted completed && !string.IsNullOrWhiteSpace(completed.Output))
        {
            return completed.Output.Trim();
        }

        if (tool.State is ToolStateError error && !string.IsNullOrWhiteSpace(error.Error))
        {
            return error.Error.Trim();
        }

        if (TryGetJsonString(tool.Metadata, "prompt", out var prompt))
        {
            return prompt;
        }

        if (tool.State is ToolStateRunning running && !string.IsNullOrWhiteSpace(running.Title))
        {
            return running.Title.Trim();
        }

        return null;
    }

    private static string ResolveUnknownToolName(ToolPart tool, ToolStateUnknown unknown)
        => !string.IsNullOrWhiteSpace(tool.Tool) ? tool.Tool.Trim() : (string.IsNullOrWhiteSpace(unknown.Status) ? "tool" : unknown.Status.Trim());

    private static string ResolveUnknownToolOutput(ToolPart tool, ToolStateUnknown unknown)
        => string.IsNullOrWhiteSpace(tool.Tool) ? $"[{unknown.Status}] tool" : $"[{unknown.Status}] {tool.Tool}";

    private static bool TryGetJsonString(IReadOnlyDictionary<string, JsonElement>? values, string key, out string value)
    {
        value = string.Empty;
        if (values is null || !values.TryGetValue(key, out var element))
        {
            return false;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                value = (element.GetString() ?? string.Empty).Trim();
                break;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                value = element.ToString().Trim();
                break;
            default:
                return false;
        }

        return !string.IsNullOrWhiteSpace(value);
    }

    private static string ResolveSessionStatusText(SessionStatus? status) => status switch
    {
        SessionStatusBusy => "运行中",
        SessionStatusRetry retry => $"重试 {retry.Attempt}",
        SessionStatusIdle => "空闲",
        _ => "未知",
    };

    private async Task<SubagentDetails> GetSubagentDetailsAsync(
        string sessionId,
        string directory,
        long sessionCreatedAt,
        long sessionUpdatedAt,
        CancellationToken ct)
    {
        var messages = await _client.Sessions
            .MessagesAsync(sessionId, directory: directory, ct: ct)
            .ConfigureAwait(false);

        string? agentName = null;
        string? modelName = null;
        long? startedAt = null;
        long? endedAt = null;

        foreach (var message in messages)
        {
            switch (message.Info)
            {
                case UserMessageWrapper user:
                    startedAt ??= user.Value.Time.Created;
                    endedAt = Math.Max(endedAt ?? long.MinValue, user.Value.Time.Created);
                    if (!string.IsNullOrWhiteSpace(user.Value.Agent))
                    {
                        agentName = user.Value.Agent.Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(user.Value.Model.ModelID) && string.IsNullOrWhiteSpace(modelName))
                    {
                        modelName = user.Value.Model.ModelID.Trim();
                    }
                    break;

                case AssistantMessageWrapper assistant:
                    startedAt ??= assistant.Value.Time.Created;
                    endedAt = Math.Max(
                        endedAt ?? long.MinValue,
                        assistant.Value.Time.Completed ?? assistant.Value.Time.Created);
                    if (!string.IsNullOrWhiteSpace(assistant.Value.ModelID))
                    {
                        modelName = assistant.Value.ModelID.Trim();
                    }
                    break;
            }

            foreach (var part in message.Parts)
            {
                if (TryResolveAgentName(part, out var resolvedAgentName))
                {
                    agentName = resolvedAgentName;
                }

                if (TryResolvePartTimestamp(part, out var timestamp))
                {
                    startedAt ??= timestamp;
                    endedAt = Math.Max(endedAt ?? long.MinValue, timestamp);
                }
            }
        }

        var contentSections = new List<string>();
        foreach (var message in messages)
        {
            var blockLines = new List<string>();
            foreach (var part in message.Parts)
            {
                var segment = GetPartContent(part);
                if (!string.IsNullOrWhiteSpace(segment))
                {
                    blockLines.Add(segment.Trim());
                }
            }

            if (blockLines.Count == 0)
            {
                continue;
            }

            var heading = message.Info switch
            {
                UserMessageWrapper => "### User",
                AssistantMessageWrapper => "### Assistant",
                _ => "### Message"
            };

            contentSections.Add($"{heading}\n\n{string.Join("\n\n", blockLines)}");
        }

        return new SubagentDetails(
            agentName ?? "subagent",
            modelName ?? "unknown",
            contentSections.Count > 0 ? string.Join("\n\n---\n\n", contentSections) : "暂无消息",
            Math.Max((endedAt ?? sessionUpdatedAt) - (startedAt ?? sessionCreatedAt), 0));
    }

    private static string? GetPartContent(Part part) => part switch
    {
        TextPart tp when !string.IsNullOrWhiteSpace(tp.Text) => tp.Text.Trim(),
        ReasoningPart rp when !string.IsNullOrWhiteSpace(rp.Text) => rp.Text.Trim(),
        FilePart fp => $"附件：{fp.Filename ?? fp.Url}",
        ToolPart tool => BuildToolMarkdown(tool),
        AgentPart agent => string.IsNullOrWhiteSpace(agent.Name) ? "subagent" : $"Agent：{agent.Name.Trim()}",
        SubtaskPart subtask when !string.IsNullOrWhiteSpace(subtask.Description) => $"子任务：{subtask.Description.Trim()}",
        SubtaskPart subtask => $"子任务 Agent：{subtask.Agent}",
        _ => null,
    };

    private static bool TryResolveAgentName(Part part, out string agentName)
    {
        agentName = part switch
        {
            AgentPart agent when !string.IsNullOrWhiteSpace(agent.Name) => agent.Name.Trim(),
            SubtaskPart subtask when !string.IsNullOrWhiteSpace(subtask.Agent) => subtask.Agent.Trim(),
            _ => string.Empty,
        };

        return !string.IsNullOrWhiteSpace(agentName);
    }

    private static bool TryResolvePartTimestamp(Part part, out long timestamp)
    {
        timestamp = part switch
        {
            TextPart text when text.Time?.End is long end => end,
            TextPart text when text.Time?.Start is long start => start,
            ReasoningPart reasoning when reasoning.Time.End is long end => end,
            ReasoningPart reasoning => reasoning.Time.Start,
            RetryPart retry => retry.Time.Created,
            _ => 0,
        };

        return timestamp > 0;
    }

    private static string BuildToolContent(RemoteBlock block)
    {
        if (block.Question is not null)
        {
            return block.ToolOutput ?? "Question";
        }

        var name = block.ToolName ?? "tool";
        var state = block.ToolState?.ToString() ?? "Pending";
        var output = block.ToolOutput ?? string.Empty;
        return $"[{state}] {name}\n{output}";
    }

    private static string BuildToolMarkdown(ToolPart tool)
    {
        if (IsQuestionTool(tool))
        {
            return BuildQuestionMarkdown(tool);
        }

        var state = tool.State switch
        {
            ToolStatePending => "Pending",
            ToolStateRunning => "Running",
            ToolStateCompleted => "Completed",
            ToolStateError => "Error",
            _ => "Unknown"
        };

        var detail = tool.State switch
        {
            ToolStateCompleted completed when !string.IsNullOrWhiteSpace(completed.Output) => completed.Output.Trim(),
            ToolStateError error when !string.IsNullOrWhiteSpace(error.Error) => error.Error.Trim(),
            ToolStateRunning running when !string.IsNullOrWhiteSpace(running.Title) => running.Title.Trim(),
            _ => string.Empty
        };

        return string.IsNullOrWhiteSpace(detail)
            ? $"**Tool** `{tool.Tool}` [{state}]"
            : $"**Tool** `{tool.Tool}` [{state}]\n\n```text\n{detail}\n```";
    }

    private static string BuildQuestionSummary(ToolPart tool)
        => string.IsNullOrWhiteSpace(BuildQuestionMarkdown(tool)) ? "Question" : BuildQuestionMarkdown(tool);

    private static RemoteQuestion? TryBuildRemoteQuestion(ToolPart tool)
    {
        if (!IsQuestionTool(tool))
        {
            return null;
        }

        var items = TryGetQuestionItems(tool.State);
        if (items.Count == 0)
        {
            return null;
        }

        var title = ResolveQuestionTitle(tool);
        return new RemoteQuestion(
            tool.CallID,
            title,
            items.Select((item, index) => new RemoteQuestionItem(
                $"{tool.CallID}:{index}",
                item.Header,
                item.Question,
                item.Multiple,
                item.Custom,
                item.Options.Select(option => new RemoteQuestionOption(
                    option.Label,
                    option.Description,
                    option.Value)).ToArray())).ToArray());
    }

    private static string ResolveQuestionTitle(ToolPart tool)
    {
        if (tool.State is ToolStateRunning running && !string.IsNullOrWhiteSpace(running.Title))
        {
            return running.Title.Trim();
        }

        return "等待回答的问题";
    }

    private static string BuildQuestionMarkdown(ToolPart tool)
    {
        var state = tool.State switch
        {
            ToolStatePending => "Pending",
            ToolStateRunning => "Running",
            ToolStateCompleted => "Completed",
            ToolStateError => "Error",
            _ => "Unknown"
        };

        var lines = new List<string> { $"**Question** [{state}]" };
        var questions = TryGetQuestionItems(tool.State);
        if (questions.Count == 0)
        {
            return lines[0];
        }

        for (var i = 0; i < questions.Count; i++)
        {
            var q = questions[i];
            lines.Add(string.Empty);
            lines.Add($"{i + 1}. {q.Header}");
            if (!string.IsNullOrWhiteSpace(q.Question))
            {
                lines.Add(q.Question);
            }

            foreach (var option in q.Options)
            {
                var description = string.IsNullOrWhiteSpace(option.Description) ? string.Empty : $" - {option.Description}";
                lines.Add($"- {option.Label}{description}");
            }

            if (q.Custom)
            {
                lines.Add("- 自定义答案");
            }
        }

        return string.Join("\n", lines);
    }

    private static IReadOnlyList<QuestionSnapshot> TryGetQuestionItems(OcToolState state)
    {
        if (state is not ToolStatePending pending)
        {
            if (state is not ToolStateRunning running)
            {
                return [];
            }

            return ParseQuestionItems(running.Input);
        }

        return ParseQuestionItems(pending.Input);
    }

    private static IReadOnlyList<QuestionSnapshot> ParseQuestionItems(IReadOnlyDictionary<string, JsonElement> input)
    {
        if (!input.TryGetValue("questions", out var questionsElement) || questionsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<QuestionSnapshot>();
        foreach (var questionElement in questionsElement.EnumerateArray())
        {
            var header = GetJsonString(questionElement, "header") ?? "Question";
            var question = GetJsonString(questionElement, "question") ?? string.Empty;
            var multiple = GetJsonBool(questionElement, "multiple");
            var custom = !GetJsonBool(questionElement, "custom", defaultValue: true) ? false : true;
            var options = new List<QuestionOptionSnapshot>();

            if (questionElement.TryGetProperty("options", out var optionsElement) && optionsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var optionElement in optionsElement.EnumerateArray())
                {
                    options.Add(new QuestionOptionSnapshot(
                        GetJsonString(optionElement, "label") ?? string.Empty,
                        GetJsonString(optionElement, "description"),
                        GetJsonString(optionElement, "value") ?? GetJsonString(optionElement, "label") ?? string.Empty));
                }
            }

            result.Add(new QuestionSnapshot(header, question, multiple, custom, options));
        }

        return result;
    }

    private static string? GetJsonString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool GetJsonBool(JsonElement element, string propertyName, bool defaultValue = false)
        => element.TryGetProperty(propertyName, out var property)
            ? property.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(property.GetString(), out var value) => value,
                _ => defaultValue,
            }
            : defaultValue;

    private sealed record QuestionSnapshot(
        string Header,
        string Question,
        bool Multiple,
        bool Custom,
        IReadOnlyList<QuestionOptionSnapshot> Options);

    private sealed record QuestionOptionSnapshot(
        string Label,
        string? Description,
        string Value);

    private sealed record SubagentDetails(
        string AgentName,
        string ModelName,
        string Content,
        long DurationMs);

    private sealed class StreamedTextPartState
    {
        public string SourceText { get; set; } = string.Empty;
        public Dictionary<string, string> EmittedTextBySegmentId { get; } = new(StringComparer.Ordinal);
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
