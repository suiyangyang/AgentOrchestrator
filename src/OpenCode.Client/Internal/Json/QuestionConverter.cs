using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCode.Client.Models;

namespace OpenCode.Client.Internal.Json;

public sealed class QuestionConverter : JsonConverter<QuestionRequest>
{
    public override QuestionRequest? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("id", out var idProp) || !root.TryGetProperty("sessionID", out _))
        {
            throw new JsonException("Missing question identity fields");
        }

        var json = root.GetRawText();
        return JsonSerializer.Deserialize<QuestionRequest>(json, options);
    }

    public override void Write(Utf8JsonWriter writer, QuestionRequest value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, value.GetType(), options);
}
