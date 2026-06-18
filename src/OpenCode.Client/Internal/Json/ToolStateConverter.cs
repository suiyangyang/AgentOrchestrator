using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCode.Client.Models;

namespace OpenCode.Client.Internal.Json;

/// <summary>
/// Deserializes ToolState based on the "status" field.
/// </summary>
public sealed class ToolStateConverter : JsonConverter<ToolState>
{
    public override ToolState? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("status", out var statusProp))
            throw new JsonException("Missing 'status' property on ToolState");

        var status = statusProp.GetString() ?? throw new JsonException("'status' property is null");
        var json = root.GetRawText();

        Type? targetType = status switch
        {
            "pending" => typeof(ToolStatePending),
            "running" => typeof(ToolStateRunning),
            "completed" => typeof(ToolStateCompleted),
            "error" => typeof(ToolStateError),
            _ => null,
        };

        if (targetType is null)
        {
            return new ToolStateUnknown
            {
                Status = status,
                Raw = root.Clone(),
            };
        }

        return (ToolState?)JsonSerializer.Deserialize(json, targetType, options);
    }

    public override void Write(Utf8JsonWriter writer, ToolState value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
