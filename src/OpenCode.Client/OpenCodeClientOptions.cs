using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCode.Client;

/// <summary>
/// Configuration options for the OpenCode HTTP client.
/// </summary>
public sealed class OpenCodeClientOptions
{
    /// <summary>
    /// Base URL of the OpenCode server. Defaults to http://127.0.0.1:4096.
    /// </summary>
    public Uri BaseUrl { get; set; } = new("http://127.0.0.1:4096");

    /// <summary>
    /// Optional authentication credentials. When null, requests are unauthenticated.
    /// </summary>
    public OpenCodeAuth? Auth { get; set; }

    /// <summary>
    /// Request timeout. Defaults to 100 seconds to match OpenCode's streaming timeout.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);

    /// <summary>
    /// Default headers added to every outgoing request.
    /// </summary>
    public IDictionary<string, string> DefaultHeaders { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Optional custom JSON serializer options. When null, source-generated defaults are used.
    /// </summary>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }
}

/// <summary>
/// HTTP Basic authentication credentials for the OpenCode server.
/// </summary>
public sealed class OpenCodeAuth
{
    /// <summary>
    /// Username. Defaults to "opencode".
    /// </summary>
    public string Username { get; init; } = "opencode";

    /// <summary>
    /// Password for basic authentication.
    /// </summary>
    public required string Password { get; init; }
}
