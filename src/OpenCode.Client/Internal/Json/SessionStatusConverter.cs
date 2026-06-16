using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCode.Client.Models;

namespace OpenCode.Client.Internal.Json;

/// <summary>
/// Deserializes SessionStatus based on the "type" field.
/// </summary>
public sealed class SessionStatusConverter : JsonConverter<SessionStatus>
{
    public override SessionStatus? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeProp))
            throw new JsonException("Missing 'type' property on SessionStatus");

        var type = typeProp.GetString() ?? throw new JsonException("'type' property is null");
        var json = root.GetRawText();

        Type targetType = type switch
        {
            "idle" => typeof(SessionStatusIdle),
            "retry" => typeof(SessionStatusRetry),
            "busy" => typeof(SessionStatusBusy),
            _ => throw new JsonException($"Unknown session status type: {type}"),
        };

        return (SessionStatus?)JsonSerializer.Deserialize(json, targetType, options);
    }

    public override void Write(Utf8JsonWriter writer, SessionStatus value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
