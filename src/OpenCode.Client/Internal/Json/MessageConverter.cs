using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCode.Client.Models;

namespace OpenCode.Client.Internal.Json;

/// <summary>
/// Deserializes Message (UserMessage | AssistantMessage) based on the "role" field.
/// </summary>
public sealed class MessageConverter : JsonConverter<Message>
{
    public override Message? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("role", out var roleProp))
            throw new JsonException("Missing 'role' property on Message");

        var role = roleProp.GetString() ?? throw new JsonException("'role' property is null");
        var json = root.GetRawText();

        return role switch
        {
            "user" => new UserMessageWrapper
            {
                Value = JsonSerializer.Deserialize<UserMessage>(json, options)!
            },
            "assistant" => new AssistantMessageWrapper
            {
                Value = JsonSerializer.Deserialize<AssistantMessage>(json, options)!
            },
            _ => throw new JsonException($"Unknown message role: {role}"),
        };
    }

    public override void Write(Utf8JsonWriter writer, Message value, JsonSerializerOptions options)
    {
        if (value is UserMessageWrapper uw)
            JsonSerializer.Serialize(writer, uw.Value, options);
        else if (value is AssistantMessageWrapper aw)
            JsonSerializer.Serialize(writer, aw.Value, options);
        else
            throw new JsonException($"Unknown message type: {value.GetType()}");
    }
}
