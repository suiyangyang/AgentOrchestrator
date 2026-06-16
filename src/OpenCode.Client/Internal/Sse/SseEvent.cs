namespace OpenCode.Client.Internal.Sse;

/// <summary>
/// Represents a parsed Server-Sent Event.
/// </summary>
public sealed class SseEvent
{
    /// <summary>
    /// The event type from the "event:" line, or null if not specified.
    /// </summary>
    public string? Event { get; init; }

    /// <summary>
    /// The event ID from the "id:" line, or null if not specified.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// The event data payload from the "data:" line(s), concatenated with \n for multi-line data.
    /// </summary>
    public required string Data { get; init; }

    /// <summary>
    /// The retry interval from the "retry:" line, or null if not specified.
    /// </summary>
    public TimeSpan? Retry { get; init; }
}
