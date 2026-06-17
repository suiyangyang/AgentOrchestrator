using System.Text.Json;

namespace OpenCode.Client.Models.Events;

/// <summary>
/// Abstract base for all server event payloads.
/// </summary>
public abstract record ServerEvent
{
    public abstract string Type { get; init; }
}

/// <summary>
/// Wraps a ServerEvent with its directory context (for /global/event SSE).
/// </summary>
public sealed record GlobalEvent
{
    public required string Directory { get; init; }
    public required ServerEvent Payload { get; init; }
}

// --- Concrete event types ---

public sealed record EventServerInstanceDisposed : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.ServerInstanceDisposed;
    public required EventServerInstanceDisposedPayload Properties { get; init; }
}

public sealed record EventServerInstanceDisposedPayload
{
    public required string Directory { get; init; }
}

public sealed record EventInstallationUpdated : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.InstallationUpdated;
    public required EventInstallationUpdatedPayload Properties { get; init; }
}

public sealed record EventInstallationUpdatedPayload
{
    public required string Version { get; init; }
}

public sealed record EventInstallationUpdateAvailable : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.InstallationUpdateAvailable;
    public required EventInstallationUpdateAvailablePayload Properties { get; init; }
}

public sealed record EventInstallationUpdateAvailablePayload
{
    public required string Version { get; init; }
}

public sealed record EventLspClientDiagnostics : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.LspClientDiagnostics;
    public required EventLspClientDiagnosticsPayload Properties { get; init; }
}

public sealed record EventLspClientDiagnosticsPayload
{
    public required string ServerID { get; init; }
    public required string Path { get; init; }
}

public sealed record EventLspUpdated : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.LspUpdated;
    public required IReadOnlyDictionary<string, JsonElement> Properties { get; init; }
}

public sealed record EventMessageUpdated : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.MessageUpdated;
    public required EventMessageUpdatedPayload Properties { get; init; }
}

public sealed record EventMessageUpdatedPayload
{
    public required Message Info { get; init; }
}

public sealed record EventMessageRemoved : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.MessageRemoved;
    public required EventMessageRemovedPayload Properties { get; init; }
}

public sealed record EventMessageRemovedPayload
{
    public required string SessionID { get; init; }
    public required string MessageID { get; init; }
}

public sealed record EventMessagePartUpdated : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.MessagePartUpdated;
    public required EventMessagePartUpdatedPayload Properties { get; init; }
}

public sealed record EventMessagePartUpdatedPayload
{
    public required Part Part { get; init; }
    public string? Delta { get; init; }
}

public sealed record EventMessagePartDelta : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.MessagePartDelta;
    public required EventMessagePartDeltaPayload Properties { get; init; }
}

public sealed record EventMessagePartDeltaPayload
{
    public required string SessionID { get; init; }
    public required string MessageID { get; init; }
    public required string PartID { get; init; }
    public required string Field { get; init; }
    public required string Delta { get; init; }
}

public sealed record EventMessagePartRemoved : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.MessagePartRemoved;
    public required EventMessagePartRemovedPayload Properties { get; init; }
}

public sealed record EventMessagePartRemovedPayload
{
    public required string SessionID { get; init; }
    public required string MessageID { get; init; }
    public required string PartID { get; init; }
}

public sealed record EventPermissionUpdated : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.PermissionUpdated;
    public required Permission Properties { get; init; }
}

public sealed record EventPermissionReplied : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.PermissionReplied;
    public required EventPermissionRepliedPayload Properties { get; init; }
}

public sealed record EventPermissionRepliedPayload
{
    public required string SessionID { get; init; }
    public required string PermissionID { get; init; }
    public required string Response { get; init; }
}

public sealed record EventSessionStatus : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.SessionStatus;
    public required EventSessionStatusPayload Properties { get; init; }
}

public sealed record EventSessionStatusPayload
{
    public required string SessionID { get; init; }
    public required SessionStatus Status { get; init; }
}

public sealed record EventSessionIdle : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.SessionIdle;
    public required EventSessionIdlePayload Properties { get; init; }
}

public sealed record EventSessionIdlePayload
{
    public required string SessionID { get; init; }
}

public sealed record EventSessionCompacted : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.SessionCompacted;
    public required EventSessionCompactedPayload Properties { get; init; }
}

public sealed record EventSessionCompactedPayload
{
    public required string SessionID { get; init; }
}

public sealed record EventFileEdited : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.FileEdited;
    public required EventFileEditedPayload Properties { get; init; }
}

public sealed record EventFileEditedPayload
{
    public required string File { get; init; }
}

public sealed record EventTodoUpdated : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.TodoUpdated;
    public required EventTodoUpdatedPayload Properties { get; init; }
}

public sealed record EventTodoUpdatedPayload
{
    public required string SessionID { get; init; }
    public required IReadOnlyList<Todo> Todos { get; init; }
}

public sealed record EventCommandExecuted : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.CommandExecuted;
    public required EventCommandExecutedPayload Properties { get; init; }
}

public sealed record EventCommandExecutedPayload
{
    public required string Name { get; init; }
    public required string SessionID { get; init; }
    public required string Arguments { get; init; }
    public required string MessageID { get; init; }
}

public sealed record EventSessionCreated : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.SessionCreated;
    public required EventSessionCreatedPayload Properties { get; init; }
}

public sealed record EventSessionCreatedPayload
{
    public required Session Info { get; init; }
}

public sealed record EventSessionUpdated : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.SessionUpdated;
    public required EventSessionUpdatedPayload Properties { get; init; }
}

public sealed record EventSessionUpdatedPayload
{
    public required Session Info { get; init; }
}

public sealed record EventSessionDeleted : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.SessionDeleted;
    public required EventSessionDeletedPayload Properties { get; init; }
}

public sealed record EventSessionDeletedPayload
{
    public required Session Info { get; init; }
}

public sealed record EventSessionDiff : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.SessionDiff;
    public required EventSessionDiffPayload Properties { get; init; }
}

public sealed record EventSessionDiffPayload
{
    public required string SessionID { get; init; }
    public required IReadOnlyList<FileDiff> Diff { get; init; }
}

public sealed record EventSessionError : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.SessionError;
    public required EventSessionErrorPayload Properties { get; init; }
}

public sealed record EventSessionErrorPayload
{
    public string? SessionID { get; init; }
    public JsonElement? Error { get; init; }
}

public sealed record EventFileWatcherUpdated : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.FileWatcherUpdated;
    public required EventFileWatcherUpdatedPayload Properties { get; init; }
}

public sealed record EventFileWatcherUpdatedPayload
{
    public required string File { get; init; }
    public required string Event { get; init; }
}

public sealed record EventVcsBranchUpdated : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.VcsBranchUpdated;
    public required EventVcsBranchUpdatedPayload Properties { get; init; }
}

public sealed record EventVcsBranchUpdatedPayload
{
    public string? Branch { get; init; }
}

public sealed record EventTuiPromptAppend : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.TuiPromptAppend;
    public required EventTuiPromptAppendPayload Properties { get; init; }
}

public sealed record EventTuiPromptAppendPayload
{
    public required string Text { get; init; }
}

public sealed record EventTuiCommandExecute : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.TuiCommandExecute;
    public required EventTuiCommandExecutePayload Properties { get; init; }
}

public sealed record EventTuiCommandExecutePayload
{
    // Can be either one of the known literals OR an arbitrary string
    public required string Command { get; init; }
}

public sealed record EventTuiToastShow : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.TuiToastShow;
    public required EventTuiToastShowPayload Properties { get; init; }
}

public sealed record EventTuiToastShowPayload
{
    public string? Title { get; init; }
    public required string Message { get; init; }
    public required string Variant { get; init; }
    public long? Duration { get; init; }
}

public sealed record EventPtyCreated : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.PtyCreated;
    public required EventPtyCreatedPayload Properties { get; init; }
}

public sealed record EventPtyCreatedPayload
{
    public required Pty Info { get; init; }
}

public sealed record EventPtyUpdated : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.PtyUpdated;
    public required EventPtyUpdatedPayload Properties { get; init; }
}

public sealed record EventPtyUpdatedPayload
{
    public required Pty Info { get; init; }
}

public sealed record EventPtyExited : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.PtyExited;
    public required EventPtyExitedPayload Properties { get; init; }
}

public sealed record EventPtyExitedPayload
{
    public required string Id { get; init; }
    public long ExitCode { get; init; }
}

public sealed record EventPtyDeleted : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.PtyDeleted;
    public required EventPtyDeletedPayload Properties { get; init; }
}

public sealed record EventPtyDeletedPayload
{
    public required string Id { get; init; }
}

public sealed record EventServerConnected : ServerEvent
{
    public override string Type { get; init; } = EventTypeConstants.ServerConnected;
    public required IReadOnlyDictionary<string, JsonElement> Properties { get; init; }
}

/// <summary>
/// Fallback for events whose discriminator is not in the whitelist. Keeps the
/// SSE stream alive when the server emits new event types we don't classify.
/// The raw payload is preserved in <see cref="Properties"/> for diagnostics.
/// </summary>
public sealed record EventUnknown : ServerEvent
{
    public override string Type { get; init; } = "";
    public required IReadOnlyDictionary<string, JsonElement> Properties { get; init; }
}
