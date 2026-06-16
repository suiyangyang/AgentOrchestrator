using System.Runtime.CompilerServices;
using System.Text.Json;
using OpenCode.Client.Internal;
using OpenCode.Client.Internal.Json;
using OpenCode.Client.Internal.Sse;
using OpenCode.Client.Models;
using OpenCode.Client.Models.Events;

namespace OpenCode.Client.Services;

internal static class QueryHelpers
{
    public static KeyValuePair<string, string?>[] WithDirectory(string? directory)
        => directory is null ? [] : [new("directory", directory)];
}

internal sealed class GlobalService : HttpServiceBase, IGlobalService
{
    public GlobalService(HttpClient http, OpenCodeClientOptions options, OpenCodeJsonSerializerContext json)
        : base(http, options, json) { }

    public async Task<IAsyncEnumerable<GlobalEvent>> EventAsync(CancellationToken ct = default)
    {
        var stream = await GetStreamAsync("/global/event", ct: ct).ConfigureAwait(false);
        return ParseSseStream(stream, ct);
    }

    private async IAsyncEnumerable<GlobalEvent> ParseSseStream(
        Stream stream, [EnumeratorCancellation] CancellationToken ct)
    {
        var options = new JsonSerializerOptions(Json.Options);
        options.Converters.Add(new EventConverter());
        options.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString;

        await foreach (var sse in SseStreamReader.ReadAsync(stream, ct).ConfigureAwait(false))
        {
            var globalEvent = JsonSerializer.Deserialize<GlobalEvent>(sse.Data, options);
            if (globalEvent is not null)
                yield return globalEvent;
        }
    }
}

internal sealed class ProjectService : HttpServiceBase, IProjectService
{
    public ProjectService(HttpClient http, OpenCodeClientOptions options, OpenCodeJsonSerializerContext json)
        : base(http, options, json) { }

    public Task<IReadOnlyList<Project>> ListAsync(string? directory = null, CancellationToken ct = default)
        => GetAsync<IReadOnlyList<Project>>("/project", QueryHelpers.WithDirectory(directory), ct)!;

    public Task<Project> CurrentAsync(string? directory = null, CancellationToken ct = default)
        => GetAsync<Project>("/project/current", QueryHelpers.WithDirectory(directory), ct)!;
}

internal sealed class PathService : HttpServiceBase, IPathService
{
    public PathService(HttpClient http, OpenCodeClientOptions options, OpenCodeJsonSerializerContext json)
        : base(http, options, json) { }

    public Task<PathInfo> GetAsync(string? directory = null, CancellationToken ct = default)
        => GetAsync<PathInfo>("/path", QueryHelpers.WithDirectory(directory), ct)!;
}

internal sealed class VcsService : HttpServiceBase, IVcsService
{
    public VcsService(HttpClient http, OpenCodeClientOptions options, OpenCodeJsonSerializerContext json)
        : base(http, options, json) { }

    public Task<VcsInfo> GetAsync(string? directory = null, CancellationToken ct = default)
        => GetAsync<VcsInfo>("/vcs", QueryHelpers.WithDirectory(directory), ct)!;
}

internal sealed class InstanceService : HttpServiceBase, IInstanceService
{
    public InstanceService(HttpClient http, OpenCodeClientOptions options, OpenCodeJsonSerializerContext json)
        : base(http, options, json) { }

    public async Task<bool> DisposeAsync(CancellationToken ct = default)
    {
        await PostNoContentAsync("/instance/dispose", ct: ct).ConfigureAwait(false);
        return true;
    }
}
