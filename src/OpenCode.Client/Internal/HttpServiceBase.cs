using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace OpenCode.Client.Internal;

/// <summary>
/// Base class for all HTTP service implementations.
/// Provides typed GET/POST/PATCH/PUT/DELETE helpers.
/// </summary>
internal abstract class HttpServiceBase
{
    protected HttpClient Http { get; }
    protected OpenCodeClientOptions Options { get; }
    protected JsonSerializerContext Json { get; }

    protected HttpServiceBase(HttpClient http, OpenCodeClientOptions options, JsonSerializerContext json)
    {
        Http = http;
        Options = options;
        Json = json;
    }

    protected async Task<TResponse?> GetAsync<TResponse>(
        string path,
        IReadOnlyList<KeyValuePair<string, string?>>? query = null,
        CancellationToken ct = default)
    {
        var uri = BuildUri(path, query);
        using var response = await Http.GetAsync(uri, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await DeserializeResponseAsync<TResponse>(response, ct).ConfigureAwait(false);
    }

    protected async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string path,
        TRequest? body,
        IReadOnlyList<KeyValuePair<string, string?>>? query = null,
        CancellationToken ct = default)
    {
        var uri = BuildUri(path, query);
        using var content = SerializeBody(body);
        using var response = await Http.PostAsync(uri, content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await DeserializeResponseAsync<TResponse>(response, ct).ConfigureAwait(false);
    }

    protected async Task<TResponse?> PatchAsync<TRequest, TResponse>(
        string path,
        TRequest? body,
        IReadOnlyList<KeyValuePair<string, string?>>? query = null,
        CancellationToken ct = default)
    {
        var uri = BuildUri(path, query);
        using var content = SerializeBody(body);
        using var request = new HttpRequestMessage(HttpMethod.Patch, uri) { Content = content };
        using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await DeserializeResponseAsync<TResponse>(response, ct).ConfigureAwait(false);
    }

    protected async Task<TResponse?> PutAsync<TRequest, TResponse>(
        string path,
        TRequest? body,
        IReadOnlyList<KeyValuePair<string, string?>>? query = null,
        CancellationToken ct = default)
    {
        var uri = BuildUri(path, query);
        using var content = SerializeBody(body);
        using var response = await Http.PutAsync(uri, content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await DeserializeResponseAsync<TResponse>(response, ct).ConfigureAwait(false);
    }

    protected async Task<bool> DeleteAsync(
        string path,
        IReadOnlyList<KeyValuePair<string, string?>>? query = null,
        CancellationToken ct = default)
    {
        var uri = BuildUri(path, query);
        using var response = await Http.DeleteAsync(uri, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return true;
    }

    protected async Task<TResponse?> DeleteWithResponseAsync<TResponse>(
        string path,
        IReadOnlyList<KeyValuePair<string, string?>>? query = null,
        CancellationToken ct = default)
    {
        var uri = BuildUri(path, query);
        using var response = await Http.DeleteAsync(uri, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await DeserializeResponseAsync<TResponse>(response, ct).ConfigureAwait(false);
    }

    protected async Task PostNoContentAsync(
        string path,
        CancellationToken ct = default)
    {
        var uri = BuildUri(path);
        using var response = await Http.PostAsync(uri, null, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    protected async Task PostNoContentAsync<TRequest>(
        string path,
        TRequest? body,
        IReadOnlyList<KeyValuePair<string, string?>>? query = null,
        CancellationToken ct = default)
    {
        var uri = BuildUri(path, query);
        using var content = SerializeBody(body);
        using var response = await Http.PostAsync(uri, content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    protected async Task<Stream> GetStreamAsync(
        string path,
        IReadOnlyList<KeyValuePair<string, string?>>? query = null,
        CancellationToken ct = default)
    {
        var uri = BuildUri(path, query);
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
        var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
    }

    protected Uri BuildUri(
        string path,
        IReadOnlyList<KeyValuePair<string, string?>>? query = null)
    {
        var builder = new UriBuilder(Options.BaseUrl)
        {
            Path = Options.BaseUrl.AbsolutePath.TrimEnd('/') + "/" + path.TrimStart('/'),
        };

        if (query is { Count: > 0 })
        {
            var sb = new StringBuilder();
            var first = true;
            foreach (var kvp in query)
            {
                if (kvp.Value is null)
                    continue;

                if (!first)
                    sb.Append('&');
                first = false;

                sb.Append(WebUtility.UrlEncode(kvp.Key));
                sb.Append('=');
                sb.Append(WebUtility.UrlEncode(kvp.Value));
            }
            builder.Query = sb.ToString();
        }

        return builder.Uri;
    }

    private HttpContent? SerializeBody<T>(T? body)
    {
        if (body is null)
            return null;

        var json = JsonSerializer.Serialize(body, typeof(T), Json);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private async Task<TResponse?> DeserializeResponseAsync<TResponse>(
        HttpResponseMessage response, CancellationToken ct)
    {
        // Handle 204 No Content
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
            return default;

        // For boolean responses, parse directly
        if (typeof(TResponse) == typeof(bool) && bool.TryParse(json, out var boolResult))
        {
            return (TResponse)(object)boolResult;
        }

        // Use the context for deserialization — Custom converters are attached to the options
        var options = new JsonSerializerOptions(Json.Options)
        {
            NumberHandling = JsonSerializerOptions.Default.NumberHandling | System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
        };

        return JsonSerializer.Deserialize<TResponse>(json, options);
    }
}
