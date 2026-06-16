using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCode.Client.Models.Events;

namespace OpenCode.Client.Internal.Json;

/// <summary>
/// Deserializes ServerEvent polymorphically based on the "type" field.
/// </summary>
public sealed class EventConverter : JsonConverter<ServerEvent>
{
    public override ServerEvent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeProp))
            throw new JsonException("Missing 'type' property on ServerEvent");

        var type = typeProp.GetString() ?? throw new JsonException("'type' property is null");

        var json = root.GetRawText();

        Type targetType = type switch
        {
            EventTypeConstants.ServerInstanceDisposed => typeof(EventServerInstanceDisposed),
            EventTypeConstants.InstallationUpdated => typeof(EventInstallationUpdated),
            EventTypeConstants.InstallationUpdateAvailable => typeof(EventInstallationUpdateAvailable),
            EventTypeConstants.LspClientDiagnostics => typeof(EventLspClientDiagnostics),
            EventTypeConstants.LspUpdated => typeof(EventLspUpdated),
            EventTypeConstants.MessageUpdated => typeof(EventMessageUpdated),
            EventTypeConstants.MessageRemoved => typeof(EventMessageRemoved),
            EventTypeConstants.MessagePartUpdated => typeof(EventMessagePartUpdated),
            EventTypeConstants.MessagePartRemoved => typeof(EventMessagePartRemoved),
            EventTypeConstants.PermissionUpdated => typeof(EventPermissionUpdated),
            EventTypeConstants.PermissionReplied => typeof(EventPermissionReplied),
            EventTypeConstants.SessionStatus => typeof(EventSessionStatus),
            EventTypeConstants.SessionIdle => typeof(EventSessionIdle),
            EventTypeConstants.SessionCompacted => typeof(EventSessionCompacted),
            EventTypeConstants.FileEdited => typeof(EventFileEdited),
            EventTypeConstants.TodoUpdated => typeof(EventTodoUpdated),
            EventTypeConstants.CommandExecuted => typeof(EventCommandExecuted),
            EventTypeConstants.SessionCreated => typeof(EventSessionCreated),
            EventTypeConstants.SessionUpdated => typeof(EventSessionUpdated),
            EventTypeConstants.SessionDeleted => typeof(EventSessionDeleted),
            EventTypeConstants.SessionDiff => typeof(EventSessionDiff),
            EventTypeConstants.SessionError => typeof(EventSessionError),
            EventTypeConstants.FileWatcherUpdated => typeof(EventFileWatcherUpdated),
            EventTypeConstants.VcsBranchUpdated => typeof(EventVcsBranchUpdated),
            EventTypeConstants.TuiPromptAppend => typeof(EventTuiPromptAppend),
            EventTypeConstants.TuiCommandExecute => typeof(EventTuiCommandExecute),
            EventTypeConstants.TuiToastShow => typeof(EventTuiToastShow),
            EventTypeConstants.PtyCreated => typeof(EventPtyCreated),
            EventTypeConstants.PtyUpdated => typeof(EventPtyUpdated),
            EventTypeConstants.PtyExited => typeof(EventPtyExited),
            EventTypeConstants.PtyDeleted => typeof(EventPtyDeleted),
            EventTypeConstants.ServerConnected => typeof(EventServerConnected),
            _ => throw new JsonException($"Unknown event type: {type}"),
        };

        return (ServerEvent?)JsonSerializer.Deserialize(json, targetType, options);
    }

    public override void Write(Utf8JsonWriter writer, ServerEvent value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
