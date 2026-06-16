using System.Text.Json;
using OpenCode.Client.Internal;
using OpenCode.Client.Internal.Json;
using OpenCode.Client.Models;
using OpenCode.Client.Requests;

namespace OpenCode.Client.Services;

internal sealed class ConfigService : HttpServiceBase, IConfigService
{
    public ConfigService(HttpClient http, OpenCodeClientOptions options, OpenCodeJsonSerializerContext json)
        : base(http, options, json) { }

    public Task<Config> GetAsync(string? directory = null, CancellationToken ct = default)
        => GetAsync<Config>("/config", QueryHelpers.WithDirectory(directory), ct)!;

    public Task<Config> UpdateAsync(Config config, string? directory = null, CancellationToken ct = default)
        => PatchAsync<Config, Config>("/config", config, QueryHelpers.WithDirectory(directory), ct)!;

    public Task<ConfigProvidersResponse> GetProvidersAsync(string? directory = null, CancellationToken ct = default)
        => GetAsync<ConfigProvidersResponse>("/config/providers", QueryHelpers.WithDirectory(directory), ct)!;
}

internal sealed class ProviderService : HttpServiceBase, IProviderService
{
    public ProviderService(HttpClient http, OpenCodeClientOptions options, OpenCodeJsonSerializerContext json)
        : base(http, options, json) { }

    public Task<ProviderListResponse> ListAsync(string? directory = null, CancellationToken ct = default)
        => GetAsync<ProviderListResponse>("/provider", QueryHelpers.WithDirectory(directory), ct)!;

    public Task<IReadOnlyDictionary<string, IReadOnlyList<ProviderAuthMethod>>> AuthMethodsAsync(string? directory = null, CancellationToken ct = default)
        => GetAsync<IReadOnlyDictionary<string, IReadOnlyList<ProviderAuthMethod>>>("/provider/auth", QueryHelpers.WithDirectory(directory), ct)!;

    public Task<ProviderAuthAuthorization> OauthAuthorizeAsync(string id, long method, string? directory = null, CancellationToken ct = default)
        => PostAsync<ProviderOauthAuthorizeRequest, ProviderAuthAuthorization>(
            $"/provider/{Uri.EscapeDataString(id)}/oauth/authorize",
            new ProviderOauthAuthorizeRequest { Method = method },
            QueryHelpers.WithDirectory(directory), ct)!;

    public async Task<bool> OauthCallbackAsync(string id, long method, string? code = null, string? directory = null, CancellationToken ct = default)
    {
        var result = await PostAsync<ProviderOauthCallbackRequest, bool>(
            $"/provider/{Uri.EscapeDataString(id)}/oauth/callback",
            new ProviderOauthCallbackRequest { Method = method, Code = code },
            QueryHelpers.WithDirectory(directory), ct).ConfigureAwait(false);
        return result;
    }
}

internal sealed class CommandService : HttpServiceBase, ICommandService
{
    public CommandService(HttpClient http, OpenCodeClientOptions options, OpenCodeJsonSerializerContext json)
        : base(http, options, json) { }

    public Task<IReadOnlyList<Command>> ListAsync(string? directory = null, CancellationToken ct = default)
        => GetAsync<IReadOnlyList<Command>>("/command", QueryHelpers.WithDirectory(directory), ct)!;
}

internal sealed class AgentService : HttpServiceBase, IAgentService
{
    public AgentService(HttpClient http, OpenCodeClientOptions options, OpenCodeJsonSerializerContext json)
        : base(http, options, json) { }

    public Task<IReadOnlyList<Agent>> ListAsync(string? directory = null, CancellationToken ct = default)
        => GetAsync<IReadOnlyList<Agent>>("/agent", QueryHelpers.WithDirectory(directory), ct)!;
}

internal sealed class AuthService : HttpServiceBase, IAuthService
{
    public AuthService(HttpClient http, OpenCodeClientOptions options, OpenCodeJsonSerializerContext json)
        : base(http, options, json) { }

    public async Task<bool> SetAsync(string id, object body, string? directory = null, CancellationToken ct = default)
    {
        var result = await PutAsync<object, bool>(
            $"/auth/{Uri.EscapeDataString(id)}", body,
            QueryHelpers.WithDirectory(directory), ct).ConfigureAwait(false);
        return result;
    }
}

internal sealed class LogService : HttpServiceBase, ILogService
{
    public LogService(HttpClient http, OpenCodeClientOptions options, OpenCodeJsonSerializerContext json)
        : base(http, options, json) { }

    public async Task<bool> LogAsync(AppLogRequest body, string? directory = null, CancellationToken ct = default)
    {
        var result = await PostAsync<AppLogRequest, bool>("/log", body, QueryHelpers.WithDirectory(directory), ct).ConfigureAwait(false);
        return result;
    }
}
