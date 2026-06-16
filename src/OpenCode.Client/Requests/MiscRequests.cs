using System.Text.Json;

namespace OpenCode.Client.Requests;

public sealed record ConfigUpdateRequest
{
    public string? Schema { get; init; }
    public string? Theme { get; init; }
    public Models.KeybindsConfig? Keybinds { get; init; }
    public string? LogLevel { get; init; }
    public Models.TuiConfig? Tui { get; init; }
    public IReadOnlyDictionary<string, Models.CommandConfigEntry>? Command { get; init; }
    public Models.WatcherConfig? Watcher { get; init; }
    public IReadOnlyList<string>? Plugin { get; init; }
    public bool? Snapshot { get; init; }
    public string? Share { get; init; }
    public bool? Autoshare { get; init; }
    public object? Autoupdate { get; init; }
    public IReadOnlyList<string>? DisabledProviders { get; init; }
    public IReadOnlyList<string>? EnabledProviders { get; init; }
    public string? Model { get; init; }
    public string? SmallModel { get; init; }
    public string? Username { get; init; }
    public IReadOnlyDictionary<string, Models.AgentConfig>? Mode { get; init; }
    public IReadOnlyDictionary<string, Models.AgentConfig>? Agent { get; init; }
    public IReadOnlyDictionary<string, Models.ProviderConfig>? Provider { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Mcp { get; init; }
    public JsonElement? Formatter { get; init; }
    public JsonElement? Lsp { get; init; }
    public IReadOnlyList<string>? Instructions { get; init; }
    public string? Layout { get; init; }
    public Models.PermissionConfig? Permission { get; init; }
    public IReadOnlyDictionary<string, bool>? Tools { get; init; }
    public Models.EnterpriseConfig? Enterprise { get; init; }
    public Models.ExperimentalConfig? Experimental { get; init; }
}

public sealed record McpAddRequest
{
    public required string Name { get; init; }
    public required JsonElement Config { get; init; }
}

public sealed record McpAuthCallbackRequest
{
    public required string Code { get; init; }
}

public sealed record ProviderOauthAuthorizeRequest
{
    public long Method { get; init; }
}

public sealed record ProviderOauthCallbackRequest
{
    public long Method { get; init; }
    public string? Code { get; init; }
}

public sealed record AppLogRequest
{
    public required string Service { get; init; }
    public required string Level { get; init; }
    public required string Message { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Extra { get; init; }
}

public sealed record AuthSetRequest
{
    public required JsonElement Body { get; init; }
}

public sealed record TuiAppendPromptRequest
{
    public required string Text { get; init; }
}

public sealed record TuiExecuteCommandRequest
{
    public required string Command { get; init; }
}

public sealed record TuiShowToastRequest
{
    public string? Title { get; init; }
    public required string Message { get; init; }
    public required string Variant { get; init; }
    public long? Duration { get; init; }
}

public sealed record TuiPublishRequest
{
    public required JsonElement Body { get; init; }
}

public sealed record TuiControlResponseRequest
{
    public JsonElement? Body { get; init; }
}

public sealed record PtyCreateRequest
{
    public string? Command { get; init; }
    public IReadOnlyList<string>? Args { get; init; }
    public string? Cwd { get; init; }
    public string? Title { get; init; }
    public IReadOnlyDictionary<string, string>? Env { get; init; }
}

public sealed record PtyUpdateRequest
{
    public string? Title { get; init; }
    public PtySizeRequest? Size { get; init; }
}

public sealed record PtySizeRequest
{
    public long Rows { get; init; }
    public long Cols { get; init; }
}
