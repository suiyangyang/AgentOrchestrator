using System.Text.Json;

namespace OpenCode.Client.Models;

public sealed record McpLocalConfig
{
    public string Type { get; init; } = "local";
    public required IReadOnlyList<string> Command { get; init; }
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
    public bool? Enabled { get; init; }
    public long? Timeout { get; init; }
}

public sealed record McpRemoteConfig
{
    public string Type { get; init; } = "remote";
    public required string Url { get; init; }
    public bool? Enabled { get; init; }
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
    public JsonElement? Oauth { get; init; }
    public long? Timeout { get; init; }
}

public sealed record McpOAuthConfig
{
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public string? Scope { get; init; }
}

public abstract record McpStatus;

public sealed record McpStatusConnected : McpStatus
{
    public string Status { get; init; } = "connected";
}

public sealed record McpStatusDisabled : McpStatus
{
    public string Status { get; init; } = "disabled";
}

public sealed record McpStatusFailed : McpStatus
{
    public string Status { get; init; } = "failed";
    public required string Error { get; init; }
}

public sealed record McpStatusNeedsAuth : McpStatus
{
    public string Status { get; init; } = "needs_auth";
}

public sealed record McpStatusNeedsClientRegistration : McpStatus
{
    public string Status { get; init; } = "needs_client_registration";
    public required string Error { get; init; }
}

public sealed record McpAuthStartResponse
{
    public required string AuthorizationUrl { get; init; }
}
