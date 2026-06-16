using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCode.Client.Models;

namespace OpenCode.Client.Internal.Json;

/// <summary>
/// Deserializes FilePartSource (FileSource | SymbolSource) based on the "type" field.
/// </summary>
public sealed class FilePartSourceConverter : JsonConverter<FilePartSource>
{
    public override FilePartSource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeProp))
            throw new JsonException("Missing 'type' property on FilePartSource");

        var type = typeProp.GetString() ?? throw new JsonException("'type' property is null");
        var json = root.GetRawText();

        Type targetType = type switch
        {
            "file" => typeof(FileSource),
            "symbol" => typeof(SymbolSource),
            _ => throw new JsonException($"Unknown FilePartSource type: {type}"),
        };

        return (FilePartSource?)JsonSerializer.Deserialize(json, targetType, options);
    }

    public override void Write(Utf8JsonWriter writer, FilePartSource value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
