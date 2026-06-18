using OpenCode.Client.Internal;
using OpenCode.Client.Internal.Json;
using OpenCode.Client.Models;
using OpenCode.Client.Requests;

namespace OpenCode.Client.Services;

internal sealed class SessionService : HttpServiceBase, ISessionService
{
    public SessionService(HttpClient http, OpenCodeClientOptions options, OpenCodeJsonSerializerContext json)
        : base(http, options, json) { }

    public Task<IReadOnlyList<Session>> ListAsync(string? directory = null, CancellationToken ct = default)
        => GetAsync<IReadOnlyList<Session>>("/session", QueryHelpers.WithDirectory(directory), ct)!;

    public Task<Session> CreateAsync(SessionCreateRequest body, string? directory = null, CancellationToken ct = default)
        => PostAsync<SessionCreateRequest, Session>("/session", body, QueryHelpers.WithDirectory(directory), ct)!;

    public Task<IReadOnlyDictionary<string, SessionStatus>> StatusAsync(string? directory = null, CancellationToken ct = default)
        => GetAsync<IReadOnlyDictionary<string, SessionStatus>>("/session/status", QueryHelpers.WithDirectory(directory), ct)!;

    public Task<Session> GetAsync(string id, string? directory = null, CancellationToken ct = default)
        => GetAsync<Session>($"/session/{Uri.EscapeDataString(id)}", QueryHelpers.WithDirectory(directory), ct)!;

    public Task<bool> DeleteAsync(string id, string? directory = null, CancellationToken ct = default)
        => DeleteAsync($"/session/{Uri.EscapeDataString(id)}", QueryHelpers.WithDirectory(directory), ct);

    public Task<Session> UpdateAsync(string id, SessionUpdateRequest body, string? directory = null, CancellationToken ct = default)
        => PatchAsync<SessionUpdateRequest, Session>($"/session/{Uri.EscapeDataString(id)}", body, QueryHelpers.WithDirectory(directory), ct)!;

    public Task<IReadOnlyList<Session>> ChildrenAsync(string id, string? directory = null, CancellationToken ct = default)
        => GetAsync<IReadOnlyList<Session>>($"/session/{Uri.EscapeDataString(id)}/children", QueryHelpers.WithDirectory(directory), ct)!;

    public Task<IReadOnlyList<Todo>> TodoAsync(string id, string? directory = null, CancellationToken ct = default)
        => GetAsync<IReadOnlyList<Todo>>($"/session/{Uri.EscapeDataString(id)}/todo", QueryHelpers.WithDirectory(directory), ct)!;

    public async Task<bool> InitAsync(string id, SessionInitRequest body, string? directory = null, CancellationToken ct = default)
    {
        var result = await PostAsync<SessionInitRequest, bool>(
            $"/session/{Uri.EscapeDataString(id)}/init", body, QueryHelpers.WithDirectory(directory), ct).ConfigureAwait(false);
        return result;
    }

    public Task<Session> ForkAsync(string id, SessionForkRequest? body = null, string? directory = null, CancellationToken ct = default)
        => PostAsync<SessionForkRequest, Session>($"/session/{Uri.EscapeDataString(id)}/fork", body ?? new SessionForkRequest(), QueryHelpers.WithDirectory(directory), ct)!;

    public async Task<bool> AbortAsync(string id, string? directory = null, CancellationToken ct = default)
    {
        var result = await PostAsync<object?, bool>($"/session/{Uri.EscapeDataString(id)}/abort", null, QueryHelpers.WithDirectory(directory), ct).ConfigureAwait(false);
        return result;
    }

    public Task<Session> ShareAsync(string id, string? directory = null, CancellationToken ct = default)
        => PostAsync<object?, Session>($"/session/{Uri.EscapeDataString(id)}/share", null, QueryHelpers.WithDirectory(directory), ct)!;

    public Task<Session> UnshareAsync(string id, string? directory = null, CancellationToken ct = default)
        => DeleteWithResponseAsync<Session>($"/session/{Uri.EscapeDataString(id)}/share", QueryHelpers.WithDirectory(directory), ct)!;

    public Task<IReadOnlyList<FileDiff>> DiffAsync(string id, string? messageID = null, string? directory = null, CancellationToken ct = default)
    {
        var query = new List<KeyValuePair<string, string?>>();
        if (directory is not null) query.Add(new("directory", directory));
        if (messageID is not null) query.Add(new("messageID", messageID));
        return GetAsync<IReadOnlyList<FileDiff>>($"/session/{Uri.EscapeDataString(id)}/diff", query, ct)!;
    }

    public async Task<bool> SummarizeAsync(string id, SessionSummarizeRequest body, string? directory = null, CancellationToken ct = default)
    {
        var result = await PostAsync<SessionSummarizeRequest, bool>(
            $"/session/{Uri.EscapeDataString(id)}/summarize", body, QueryHelpers.WithDirectory(directory), ct).ConfigureAwait(false);
        return result;
    }

    public Task<IReadOnlyList<MessageWithParts>> MessagesAsync(string id, long? limit = null, string? directory = null, CancellationToken ct = default)
    {
        var query = new List<KeyValuePair<string, string?>>();
        if (directory is not null) query.Add(new("directory", directory));
        if (limit.HasValue) query.Add(new("limit", limit.Value.ToString()));
        return GetAsync<IReadOnlyList<MessageWithParts>>($"/session/{Uri.EscapeDataString(id)}/message", query, ct)!;
    }

    public Task<MessageWithParts> PromptAsync(string id, SessionPromptRequest body, string? directory = null, CancellationToken ct = default)
        => PostAsync<SessionPromptRequest, MessageWithParts>($"/session/{Uri.EscapeDataString(id)}/message", body, QueryHelpers.WithDirectory(directory), ct)!;

    public Task<MessageWithParts> GetMessageAsync(string id, string messageID, string? directory = null, CancellationToken ct = default)
        => GetAsync<MessageWithParts>($"/session/{Uri.EscapeDataString(id)}/message/{Uri.EscapeDataString(messageID)}", QueryHelpers.WithDirectory(directory), ct)!;

    public async Task PromptAsyncAsync(string id, SessionPromptRequest body, string? directory = null, CancellationToken ct = default)
    {
        await PostNoContentAsync($"/session/{Uri.EscapeDataString(id)}/prompt_async", body, QueryHelpers.WithDirectory(directory), ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<QuestionRequest>> QuestionsAsync(string id, string? directory = null, CancellationToken ct = default)
        => GetAsync<IReadOnlyList<QuestionRequest>>($"/session/{Uri.EscapeDataString(id)}/question", QueryHelpers.WithDirectory(directory), ct)!;

    public async Task<bool> ReplyQuestionAsync(string id, string requestID, QuestionReplyRequest body, string? directory = null, CancellationToken ct = default)
    {
        var result = await PostAsync<QuestionReplyRequest, bool>(
            $"/session/{Uri.EscapeDataString(id)}/question/{Uri.EscapeDataString(requestID)}/reply",
            body,
            QueryHelpers.WithDirectory(directory),
            ct).ConfigureAwait(false);
        return result;
    }

    public Task<MessageWithParts> CommandAsync(string id, SessionCommandRequest body, string? directory = null, CancellationToken ct = default)
        => PostAsync<SessionCommandRequest, MessageWithParts>($"/session/{Uri.EscapeDataString(id)}/command", body, QueryHelpers.WithDirectory(directory), ct)!;

    public Task<MessageWithParts> ShellAsync(string id, SessionShellRequest body, string? directory = null, CancellationToken ct = default)
        => PostAsync<SessionShellRequest, MessageWithParts>($"/session/{Uri.EscapeDataString(id)}/shell", body, QueryHelpers.WithDirectory(directory), ct)!;

    public Task<Session> RevertAsync(string id, SessionRevertRequest body, string? directory = null, CancellationToken ct = default)
        => PostAsync<SessionRevertRequest, Session>($"/session/{Uri.EscapeDataString(id)}/revert", body, QueryHelpers.WithDirectory(directory), ct)!;

    public Task<Session> UnrevertAsync(string id, string? directory = null, CancellationToken ct = default)
        => PostAsync<object?, Session>($"/session/{Uri.EscapeDataString(id)}/unrevert", null, QueryHelpers.WithDirectory(directory), ct)!;

    public async Task<bool> RespondToPermissionAsync(string id, string permissionID, PostSessionIdPermissionsPermissionIdRequest body, string? directory = null, CancellationToken ct = default)
    {
        var result = await PostAsync<PostSessionIdPermissionsPermissionIdRequest, bool>(
            $"/session/{Uri.EscapeDataString(id)}/permissions/{Uri.EscapeDataString(permissionID)}",
            body, QueryHelpers.WithDirectory(directory), ct).ConfigureAwait(false);
        return result;
    }
}
