using OpenCode.Client.Internal;
using OpenCode.Client.Internal.Json;
using OpenCode.Client.Models;
using OpenCode.Client.Requests;

namespace OpenCode.Client.Services;

internal sealed class McpService : HttpServiceBase, IMcpService
{
    public McpService(HttpClient http, OpenCodeClientOptions options, OpenCodeJsonSerializerContext json)
        : base(http, options, json) { }

    public Task<IReadOnlyDictionary<string, McpStatus>> StatusAsync(string? directory = null, CancellationToken ct = default)
        => GetAsync<IReadOnlyDictionary<string, McpStatus>>("/mcp", QueryHelpers.WithDirectory(directory), ct)!;

    public Task<IReadOnlyDictionary<string, McpStatus>> AddAsync(McpAddRequest body, string? directory = null, CancellationToken ct = default)
        => PostAsync<McpAddRequest, IReadOnlyDictionary<string, McpStatus>>("/mcp", body, QueryHelpers.WithDirectory(directory), ct)!;

    public async Task<bool> ConnectAsync(string name, string? directory = null, CancellationToken ct = default)
    {
        var result = await PostAsync<object?, bool>($"/mcp/{Uri.EscapeDataString(name)}/connect", null, QueryHelpers.WithDirectory(directory), ct).ConfigureAwait(false);
        return result;
    }

    public async Task<bool> DisconnectAsync(string name, string? directory = null, CancellationToken ct = default)
    {
        var result = await PostAsync<object?, bool>($"/mcp/{Uri.EscapeDataString(name)}/disconnect", null, QueryHelpers.WithDirectory(directory), ct).ConfigureAwait(false);
        return result;
    }

    public Task<bool> RemoveAuthAsync(string name, string? directory = null, CancellationToken ct = default)
        => DeleteAsync($"/mcp/{Uri.EscapeDataString(name)}/auth", QueryHelpers.WithDirectory(directory), ct);

    public Task<McpAuthStartResponse> StartAuthAsync(string name, string? directory = null, CancellationToken ct = default)
        => PostAsync<object?, McpAuthStartResponse>($"/mcp/{Uri.EscapeDataString(name)}/auth", null, QueryHelpers.WithDirectory(directory), ct)!;

    public Task<McpStatus> AuthCallbackAsync(string name, McpAuthCallbackRequest body, string? directory = null, CancellationToken ct = default)
        => PostAsync<McpAuthCallbackRequest, McpStatus>($"/mcp/{Uri.EscapeDataString(name)}/auth/callback", body, QueryHelpers.WithDirectory(directory), ct)!;

    public Task<McpStatus> AuthenticateAsync(string name, string? directory = null, CancellationToken ct = default)
        => PostAsync<object?, McpStatus>($"/mcp/{Uri.EscapeDataString(name)}/auth/authenticate", null, QueryHelpers.WithDirectory(directory), ct)!;
}

internal sealed class TuiService : HttpServiceBase, ITuiService
{
    public TuiService(HttpClient http, OpenCodeClientOptions options, OpenCodeJsonSerializerContext json)
        : base(http, options, json) { }

    public async Task<bool> AppendPromptAsync(TuiAppendPromptRequest body, string? directory = null, CancellationToken ct = default)
    {
        var result = await PostAsync<TuiAppendPromptRequest, bool>("/tui/append-prompt", body, QueryHelpers.WithDirectory(directory), ct).ConfigureAwait(false);
        return result;
    }

    public async Task<bool> OpenHelpAsync(string? directory = null, CancellationToken ct = default)
    {
        var result = await PostAsync<object?, bool>("/tui/open-help", null, QueryHelpers.WithDirectory(directory), ct).ConfigureAwait(false);
        return result;
    }

    public async Task<bool> OpenSessionsAsync(string? directory = null, CancellationToken ct = default)
    {
        var result = await PostAsync<object?, bool>("/tui/open-sessions", null, QueryHelpers.WithDirectory(directory), ct).ConfigureAwait(false);
        return result;
    }

    public async Task<bool> OpenThemesAsync(string? directory = null, CancellationToken ct = default)
    {
        var result = await PostAsync<object?, bool>("/tui/open-themes", null, QueryHelpers.WithDirectory(directory), ct).ConfigureAwait(false);
        return result;
    }

    public async Task<bool> OpenModelsAsync(string? directory = null, CancellationToken ct = default)
    {
        var result = await PostAsync<object?, bool>("/tui/open-models", null, QueryHelpers.WithDirectory(directory), ct).ConfigureAwait(false);
        return result;
    }

    public async Task<bool> SubmitPromptAsync(string? directory = null, CancellationToken ct = default)
    {
        var result = await PostAsync<object?, bool>("/tui/submit-prompt", null, QueryHelpers.WithDirectory(directory), ct).ConfigureAwait(false);
        return result;
    }

    public async Task<bool> ClearPromptAsync(string? directory = null, CancellationToken ct = default)
    {
        var result = await PostAsync<object?, bool>("/tui/clear-prompt", null, QueryHelpers.WithDirectory(directory), ct).ConfigureAwait(false);
        return result;
    }

    public async Task<bool> ExecuteCommandAsync(TuiExecuteCommandRequest body, string? directory = null, CancellationToken ct = default)
    {
        var result = await PostAsync<TuiExecuteCommandRequest, bool>("/tui/execute-command", body, QueryHelpers.WithDirectory(directory), ct).ConfigureAwait(false);
        return result;
    }

    public async Task<bool> ShowToastAsync(TuiShowToastRequest body, string? directory = null, CancellationToken ct = default)
    {
        var result = await PostAsync<TuiShowToastRequest, bool>("/tui/show-toast", body, QueryHelpers.WithDirectory(directory), ct).ConfigureAwait(false);
        return result;
    }

    public async Task<bool> PublishAsync(TuiPublishRequest body, string? directory = null, CancellationToken ct = default)
    {
        // The body is already a JsonElement, serialize it as-is
        var result = await PostAsync<TuiPublishRequest, bool>("/tui/publish", body, QueryHelpers.WithDirectory(directory), ct).ConfigureAwait(false);
        return result;
    }

    public Task<TuiControlRequest> ControlNextAsync(string? directory = null, CancellationToken ct = default)
        => GetAsync<TuiControlRequest>("/tui/control/next", QueryHelpers.WithDirectory(directory), ct)!;

    public async Task<bool> ControlResponseAsync(TuiControlResponseRequest body, string? directory = null, CancellationToken ct = default)
    {
        var result = await PostAsync<TuiControlResponseRequest, bool>("/tui/control/response", body, QueryHelpers.WithDirectory(directory), ct).ConfigureAwait(false);
        return result;
    }
}

internal sealed class PtyService : HttpServiceBase, IPtyService
{
    public PtyService(HttpClient http, OpenCodeClientOptions options, OpenCodeJsonSerializerContext json)
        : base(http, options, json) { }

    public Task<IReadOnlyList<Pty>> ListAsync(string? directory = null, CancellationToken ct = default)
        => GetAsync<IReadOnlyList<Pty>>("/pty", QueryHelpers.WithDirectory(directory), ct)!;

    public Task<Pty> CreateAsync(PtyCreateRequest body, string? directory = null, CancellationToken ct = default)
        => PostAsync<PtyCreateRequest, Pty>("/pty", body, QueryHelpers.WithDirectory(directory), ct)!;

    public Task<bool> DeleteAsync(string id, string? directory = null, CancellationToken ct = default)
        => DeleteAsync($"/pty/{Uri.EscapeDataString(id)}", QueryHelpers.WithDirectory(directory), ct);

    public Task<Pty> GetAsync(string id, string? directory = null, CancellationToken ct = default)
        => GetAsync<Pty>($"/pty/{Uri.EscapeDataString(id)}", QueryHelpers.WithDirectory(directory), ct)!;

    public Task<Pty> UpdateAsync(string id, PtyUpdateRequest body, string? directory = null, CancellationToken ct = default)
        => PutAsync<PtyUpdateRequest, Pty>($"/pty/{Uri.EscapeDataString(id)}", body, QueryHelpers.WithDirectory(directory), ct)!;

    public async Task<bool> ConnectAsync(string id, string? directory = null, CancellationToken ct = default)
    {
        var result = await GetAsync<bool>($"/pty/{Uri.EscapeDataString(id)}/connect", QueryHelpers.WithDirectory(directory), ct).ConfigureAwait(false);
        return result;
    }
}
