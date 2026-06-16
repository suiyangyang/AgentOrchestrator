using System.Text.Json;

namespace OpenCode.Client.Models;

public sealed record Provider
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Source { get; init; }
    public required IReadOnlyList<string> Env { get; init; }
    public string? Key { get; init; }
    public required IReadOnlyDictionary<string, JsonElement> Options { get; init; }
    public required IReadOnlyDictionary<string, Model> Models { get; init; }
}

public sealed record Model
{
    public required string Id { get; init; }
    public required string ProviderID { get; init; }
    public required ModelApiInfo Api { get; init; }
    public required string Name { get; init; }
    public required ModelCapabilities Capabilities { get; init; }
    public required ModelCost Cost { get; init; }
    public required ModelLimit Limit { get; init; }
    public required string Status { get; init; }
    public required IReadOnlyDictionary<string, JsonElement> Options { get; init; }
    public required IReadOnlyDictionary<string, string> Headers { get; init; }
}

public sealed record ModelApiInfo
{
    public required string Id { get; init; }
    public required string Url { get; init; }
    public required string Npm { get; init; }
}

public sealed record ModelCapabilities
{
    public bool Temperature { get; init; }
    public bool Reasoning { get; init; }
    public bool Attachment { get; init; }
    public bool Toolcall { get; init; }
    public required ModelModality Input { get; init; }
    public required ModelModality Output { get; init; }
}

public sealed record ModelModality
{
    public bool Text { get; init; }
    public bool Audio { get; init; }
    public bool Image { get; init; }
    public bool Video { get; init; }
    public bool Pdf { get; init; }
}

public sealed record ModelCost
{
    public double Input { get; init; }
    public double Output { get; init; }
    public required ModelCacheCost Cache { get; init; }
    public ModelOver200KCost? ExperimentalOver200K { get; init; }
}

public sealed record ModelCacheCost
{
    public double Read { get; init; }
    public double Write { get; init; }
}

public sealed record ModelOver200KCost
{
    public double Input { get; init; }
    public double Output { get; init; }
    public required ModelCacheCost Cache { get; init; }
}

public sealed record ModelLimit
{
    public long Context { get; init; }
    public long Output { get; init; }
}

public sealed record ProviderAuthMethod
{
    public required string Type { get; init; }
    public required string Label { get; init; }
}

public sealed record ProviderAuthAuthorization
{
    public required string Url { get; init; }
    public required string Method { get; init; }
    public required string Instructions { get; init; }
}

public sealed record ProviderListResponse
{
    public required IReadOnlyList<ProviderConfigSummary> All { get; init; }
    public required IReadOnlyDictionary<string, string> Default { get; init; }
    public required IReadOnlyList<string> Connected { get; init; }
}

public sealed record ProviderConfigSummary
{
    public string? Api { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<string> Env { get; init; }
    public required string Id { get; init; }
    public string? Npm { get; init; }
    public required IReadOnlyDictionary<string, ProviderModelSummary> Models { get; init; }
}

public sealed record ProviderModelSummary
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? ReleaseDate { get; init; }
    public bool Attachment { get; init; }
    public bool Reasoning { get; init; }
    public bool Temperature { get; init; }
    public bool ToolCall { get; init; }
    public ProviderModelCost? Cost { get; init; }
    public required ProviderModelLimit Limit { get; init; }
    public ProviderModelModalities? Modalities { get; init; }
    public bool? Experimental { get; init; }
    public string? Status { get; init; }
    public required IReadOnlyDictionary<string, JsonElement> Options { get; init; }
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
    public ProviderNpmRef? Provider { get; init; }
}

public sealed record ProviderModelCost
{
    public double Input { get; init; }
    public double Output { get; init; }
    public double? CacheRead { get; init; }
    public double? CacheWrite { get; init; }
    public ProviderModelOver200KCost? ContextOver200K { get; init; }
}

public sealed record ProviderModelOver200KCost
{
    public double Input { get; init; }
    public double Output { get; init; }
    public double? CacheRead { get; init; }
    public double? CacheWrite { get; init; }
}

public sealed record ProviderModelLimit
{
    public long Context { get; init; }
    public long Output { get; init; }
}

public sealed record ProviderModelModalities
{
    public required IReadOnlyList<string> Input { get; init; }
    public required IReadOnlyList<string> Output { get; init; }
}

public sealed record ProviderNpmRef
{
    public required string Npm { get; init; }
}

public sealed record ConfigProvidersResponse
{
    public required IReadOnlyList<Provider> Providers { get; init; }
    public IReadOnlyDictionary<string, string>? Default { get; init; }
}
