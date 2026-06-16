using System.Runtime.CompilerServices;
using System.Text.Json;
using OpenCode.Client.Internal;
using OpenCode.Client.Internal.Json;
using OpenCode.Client.Internal.Sse;
using OpenCode.Client.Models;
using OpenCode.Client.Models.Events;

namespace OpenCode.Client.Services;

internal sealed class EventService : HttpServiceBase, IEventService
{
    public EventService(HttpClient http, OpenCodeClientOptions options, OpenCodeJsonSerializerContext json)
        : base(http, options, json) { }

    public async Task<IAsyncEnumerable<GlobalEvent>> SubscribeAsync(string? directory = null, CancellationToken ct = default)
    {
        var stream = await GetStreamAsync("/event", QueryHelpers.WithDirectory(directory), ct).ConfigureAwait(false);
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
            ServerEvent? serverEvent;
            try
            {
                serverEvent = JsonSerializer.Deserialize<ServerEvent>(sse.Data, options);
            }
            catch
            {
                continue;
            }

            if (serverEvent is not null)
            {
                // /event returns raw ServerEvent, wrap in GlobalEvent with empty directory
                yield return new GlobalEvent
                {
                    Directory = "",
                    Payload = serverEvent,
                };
            }
        }
    }
}
