using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCode.Client.Models;

namespace OpenCode.Client.Internal.Json;

/// <summary>
/// Deserializes Part polymorphically based on the "type" field.
/// </summary>
public sealed class PartConverter : JsonConverter<Part>
{
    public override Part? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeProp))
            throw new JsonException("Missing 'type' property on Part");

        var type = typeProp.GetString() ?? throw new JsonException("'type' property is null");
        var json = root.GetRawText();

        Type targetType = type switch
        {
            "text" => typeof(TextPart),
            "reasoning" => typeof(ReasoningPart),
            "file" => typeof(FilePart),
            "tool" => typeof(ToolPart),
            "step-start" => typeof(StepStartPart),
            "step-finish" => typeof(StepFinishPart),
            "snapshot" => typeof(SnapshotPart),
            "patch" => typeof(PatchPart),
            "agent" => typeof(AgentPart),
            "retry" => typeof(RetryPart),
            "compaction" => typeof(CompactionPart),
            "subtask" => typeof(SubtaskPart),
            _ => throw new JsonException($"Unknown part type: {type}"),
        };

        return (Part?)JsonSerializer.Deserialize(json, targetType, options);
    }

    public override void Write(Utf8JsonWriter writer, Part value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
