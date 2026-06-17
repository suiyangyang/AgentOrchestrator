namespace OpenCode.Client.Models.Events;

/// <summary>
/// String constants for all ServerEvent discriminator values.
/// </summary>
public static class EventTypeConstants
{
    public const string ServerInstanceDisposed = "server.instance.disposed";
    public const string InstallationUpdated = "installation.updated";
    public const string InstallationUpdateAvailable = "installation.update-available";
    public const string LspClientDiagnostics = "lsp.client.diagnostics";
    public const string LspUpdated = "lsp.updated";
    public const string MessageUpdated = "message.updated";
    public const string MessageRemoved = "message.removed";
    public const string MessagePartUpdated = "message.part.updated";
    public const string MessagePartDelta = "message.part.delta";
    public const string MessagePartRemoved = "message.part.removed";
    public const string PermissionUpdated = "permission.updated";
    public const string PermissionReplied = "permission.replied";
    public const string SessionStatus = "session.status";
    public const string SessionIdle = "session.idle";
    public const string SessionCompacted = "session.compacted";
    public const string FileEdited = "file.edited";
    public const string TodoUpdated = "todo.updated";
    public const string CommandExecuted = "command.executed";
    public const string SessionCreated = "session.created";
    public const string SessionUpdated = "session.updated";
    public const string SessionDeleted = "session.deleted";
    public const string SessionDiff = "session.diff";
    public const string SessionError = "session.error";
    public const string FileWatcherUpdated = "file.watcher.updated";
    public const string VcsBranchUpdated = "vcs.branch.updated";
    public const string TuiPromptAppend = "tui.prompt.append";
    public const string TuiCommandExecute = "tui.command.execute";
    public const string TuiToastShow = "tui.toast.show";
    public const string PtyCreated = "pty.created";
    public const string PtyUpdated = "pty.updated";
    public const string PtyExited = "pty.exited";
    public const string PtyDeleted = "pty.deleted";
    public const string ServerConnected = "server.connected";
}
