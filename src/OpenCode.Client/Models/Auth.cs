using System.Text.Json;

namespace OpenCode.Client.Models;

public abstract record Auth;

public sealed record OAuth : Auth
{
    public string Type { get; init; } = "oauth";
    public required string Refresh { get; init; }
    public required string Access { get; init; }
    public long Expires { get; init; }
    public string? EnterpriseUrl { get; init; }
}

public sealed record ApiAuth : Auth
{
    public string Type { get; init; } = "api";
    public required string Key { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record WellKnownAuth : Auth
{
    public string Type { get; init; } = "wellknown";
    public required string Key { get; init; }
    public required string Token { get; init; }
}
