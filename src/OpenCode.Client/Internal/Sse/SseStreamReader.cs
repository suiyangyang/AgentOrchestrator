using System.Runtime.CompilerServices;

namespace OpenCode.Client.Internal.Sse;

/// <summary>
/// Reads Server-Sent Events from a stream.
/// </summary>
public static class SseStreamReader
{
    /// <summary>
    /// Parses an SSE stream and yields individual SseEvent objects.
    /// </summary>
    public static async IAsyncEnumerable<SseEvent> ReadAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);

        string? eventType = null;
        string? eventId = null;
        var dataLines = new List<string>();
        TimeSpan? retry = null;

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);

            if (line is null)
            {
                // Stream ended. Emit any pending event.
                if (dataLines.Count > 0)
                {
                    yield return new SseEvent
                    {
                        Event = eventType,
                        Id = eventId,
                        Data = string.Join("\n", dataLines),
                        Retry = retry,
                    };
                }
                yield break;
            }

            // Empty line signals the end of an event
            if (line.Length == 0)
            {
                if (dataLines.Count > 0)
                {
                    yield return new SseEvent
                    {
                        Event = eventType,
                        Id = eventId,
                        Data = string.Join("\n", dataLines),
                        Retry = retry,
                    };
                }

                // Reset for next event
                eventType = null;
                eventId = null;
                dataLines = [];
                retry = null;
                continue;
            }

            // Comments (lines starting with ':') are ignored
            if (line.StartsWith(':'))
            {
                continue;
            }

            // Parse the field name and value
            var colonIndex = line.IndexOf(':');
            if (colonIndex < 0)
            {
                // Malformed line; treat entire line as field with empty value
                ProcessField(line.Trim(), "", ref eventType, ref eventId, dataLines, ref retry);
            }
            else
            {
                var field = line.AsSpan(0, colonIndex).Trim().ToString();
                var value = line.AsSpan(colonIndex + 1);

                // Remove a single leading space if present
                if (value.Length > 0 && value[0] == ' ')
                {
                    value = value[1..];
                }

                ProcessField(field, value.ToString(), ref eventType, ref eventId, dataLines, ref retry);
            }
        }
    }

    private static void ProcessField(
        string field,
        string value,
        ref string? eventType,
        ref string? eventId,
        List<string> dataLines,
        ref TimeSpan? retry)
    {
        switch (field)
        {
            case "event":
                eventType = value;
                break;
            case "data":
                dataLines.Add(value);
                break;
            case "id":
                eventId = value;
                break;
            case "retry":
                if (long.TryParse(value, out var ms))
                {
                    retry = TimeSpan.FromMilliseconds(ms);
                }
                break;
        }
    }
}
