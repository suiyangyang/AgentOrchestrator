using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCode.Client.Models;

namespace OpenCode.Client.Internal.Json;

/// <summary>
/// Deserializes PartInput polymorphically based on the "type" field.
/// </summary>
public sealed class PartInputConverter : JsonConverter<PartInput>
{
    public override PartInput? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeProp))
            throw new JsonException("Missing 'type' property on PartInput");

        var type = typeProp.GetString() ?? throw new JsonException("'type' property is null");
        var json = root.GetRawText();

        Type targetType = type switch
        {
            "text" => typeof(TextPartInput),
            "file" => typeof(FilePartInput),
            "agent" => typeof(AgentPartInput),
            "subtask" => typeof(SubtaskPartInput),
            _ => throw new JsonException($"Unknown PartInput type: {type}"),
        };

        return (PartInput?)JsonSerializer.Deserialize(json, targetType, options);
    }

    public override void Write(Utf8JsonWriter writer, PartInput value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}

/// <summary>
/// Deserializes McpStatus polymorphically based on the "status" field.
/// </summary>
public sealed class McpStatusConverter : JsonConverter<McpStatus>
{
    public override McpStatus? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("status", out var statusProp))
            throw new JsonException("Missing 'status' property on McpStatus");

        var status = statusProp.GetString() ?? throw new JsonException("'status' property is null");
        var json = root.GetRawText();

        Type targetType = status switch
        {
            "connected" => typeof(McpStatusConnected),
            "disabled" => typeof(McpStatusDisabled),
            "failed" => typeof(McpStatusFailed),
            "needs_auth" => typeof(McpStatusNeedsAuth),
            "needs_client_registration" => typeof(McpStatusNeedsClientRegistration),
            _ => throw new JsonException($"Unknown MCP status: {status}"),
        };

        return (McpStatus?)JsonSerializer.Deserialize(json, targetType, options);
    }

    public override void Write(Utf8JsonWriter writer, McpStatus value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}

/// <summary>
/// Deserializes Auth polymorphically based on the "type" field.
/// </summary>
public sealed class AuthConverter : JsonConverter<Auth>
{
    public override Auth? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeProp))
            throw new JsonException("Missing 'type' property on Auth");

        var type = typeProp.GetString() ?? throw new JsonException("'type' property is null");
        var json = root.GetRawText();

        Type targetType = type switch
        {
            "oauth" => typeof(OAuth),
            "api" => typeof(ApiAuth),
            "wellknown" => typeof(WellKnownAuth),
            _ => throw new JsonException($"Unknown auth type: {type}"),
        };

        return (Auth?)JsonSerializer.Deserialize(json, targetType, options);
    }

    public override void Write(Utf8JsonWriter writer, Auth value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
