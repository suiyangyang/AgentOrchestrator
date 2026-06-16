using System.Text.Json;

namespace OpenCode.Client.Models;

public sealed record Config
{
    public string? Schema { get; init; }
    public string? Theme { get; init; }
    public KeybindsConfig? Keybinds { get; init; }
    public string? LogLevel { get; init; }
    public TuiConfig? Tui { get; init; }
    public IReadOnlyDictionary<string, CommandConfigEntry>? Command { get; init; }
    public WatcherConfig? Watcher { get; init; }
    public IReadOnlyList<string>? Plugin { get; init; }
    public bool? Snapshot { get; init; }
    public string? Share { get; init; }
    public bool? Autoshare { get; init; }
    public object? Autoupdate { get; init; }
    public IReadOnlyList<string>? DisabledProviders { get; init; }
    public IReadOnlyList<string>? EnabledProviders { get; init; }
    public string? Model { get; init; }
    public string? SmallModel { get; init; }
    public string? Username { get; init; }
    public IReadOnlyDictionary<string, AgentConfig>? Mode { get; init; }
    public IReadOnlyDictionary<string, AgentConfig>? Agent { get; init; }
    public IReadOnlyDictionary<string, ProviderConfig>? Provider { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Mcp { get; init; }
    public JsonElement? Formatter { get; init; }
    public JsonElement? Lsp { get; init; }
    public IReadOnlyList<string>? Instructions { get; init; }
    public string? Layout { get; init; }
    public PermissionConfig? Permission { get; init; }
    public IReadOnlyDictionary<string, bool>? Tools { get; init; }
    public EnterpriseConfig? Enterprise { get; init; }
    public ExperimentalConfig? Experimental { get; init; }
}

public sealed record KeybindsConfig
{
    public string? Leader { get; init; }
    public string? AppExit { get; init; }
    public string? EditorOpen { get; init; }
    public string? ThemeList { get; init; }
    public string? SidebarToggle { get; init; }
    public string? ScrollbarToggle { get; init; }
    public string? UsernameToggle { get; init; }
    public string? StatusView { get; init; }
    public string? SessionExport { get; init; }
    public string? SessionNew { get; init; }
    public string? SessionList { get; init; }
    public string? SessionTimeline { get; init; }
    public string? SessionShare { get; init; }
    public string? SessionUnshare { get; init; }
    public string? SessionInterrupt { get; init; }
    public string? SessionCompact { get; init; }
    public string? MessagesPageUp { get; init; }
    public string? MessagesPageDown { get; init; }
    public string? MessagesLineUp { get; init; }
    public string? MessagesLineDown { get; init; }
    public string? MessagesHalfPageUp { get; init; }
    public string? MessagesHalfPageDown { get; init; }
    public string? MessagesFirst { get; init; }
    public string? MessagesLast { get; init; }
    public string? MessagesNext { get; init; }
    public string? MessagesPrevious { get; init; }
    public string? MessagesLastUser { get; init; }
    public string? MessagesCopy { get; init; }
    public string? MessagesUndo { get; init; }
    public string? MessagesRedo { get; init; }
    public string? MessagesToggleConceal { get; init; }
    public string? ToolDetails { get; init; }
    public string? ModelList { get; init; }
    public string? ModelCycleRecent { get; init; }
    public string? ModelCycleRecentReverse { get; init; }
    public string? CommandList { get; init; }
    public string? AgentList { get; init; }
    public string? AgentCycle { get; init; }
    public string? AgentCycleReverse { get; init; }
    public string? InputClear { get; init; }
    public string? InputForwardDelete { get; init; }
    public string? InputPaste { get; init; }
    public string? InputSubmit { get; init; }
    public string? InputNewline { get; init; }
    public string? HistoryPrevious { get; init; }
    public string? HistoryNext { get; init; }
    public string? SessionChildCycle { get; init; }
    public string? SessionChildCycleReverse { get; init; }
    public string? TerminalSuspend { get; init; }
    public string? TerminalTitleToggle { get; init; }
}

public sealed record TuiConfig
{
    public double? ScrollSpeed { get; init; }
    public TuiScrollAcceleration? ScrollAcceleration { get; init; }
    public string? DiffStyle { get; init; }
}

public sealed record TuiScrollAcceleration
{
    public bool Enabled { get; init; }
}

public sealed record CommandConfigEntry
{
    public required string Template { get; init; }
    public string? Description { get; init; }
    public string? Agent { get; init; }
    public string? Model { get; init; }
    public bool? Subtask { get; init; }
}

public sealed record WatcherConfig
{
    public IReadOnlyList<string>? Ignore { get; init; }
}

public sealed record AgentConfig
{
    public string? Model { get; init; }
    public double? Temperature { get; init; }
    public double? TopP { get; init; }
    public string? Prompt { get; init; }
    public IReadOnlyDictionary<string, bool>? Tools { get; init; }
    public bool? Disable { get; init; }
    public string? Description { get; init; }
    public string? Mode { get; init; }
    public string? Color { get; init; }
    public long? MaxSteps { get; init; }
    public AgentPermissionConfig? Permission { get; init; }
}

public sealed record AgentPermissionConfig
{
    public string? Edit { get; init; }
    public JsonElement? Bash { get; init; }
    public string? Webfetch { get; init; }
    public string? DoomLoop { get; init; }
    public string? ExternalDirectory { get; init; }
}

public sealed record ProviderConfig
{
    public string? Api { get; init; }
    public string? Name { get; init; }
    public IReadOnlyList<string>? Env { get; init; }
    public string? Id { get; init; }
    public string? Npm { get; init; }
    public IReadOnlyDictionary<string, ProviderModelConfig>? Models { get; init; }
    public IReadOnlyList<string>? Whitelist { get; init; }
    public IReadOnlyList<string>? Blacklist { get; init; }
    public ProviderOptionsConfig? Options { get; init; }
}

public sealed record ProviderModelConfig
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? ReleaseDate { get; init; }
    public bool? Attachment { get; init; }
    public bool? Reasoning { get; init; }
    public bool? Temperature { get; init; }
    public bool? ToolCall { get; init; }
    public ProviderModelConfigCost? Cost { get; init; }
    public ProviderModelConfigLimit? Limit { get; init; }
    public ProviderModelModalities? Modalities { get; init; }
    public bool? Experimental { get; init; }
    public string? Status { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Options { get; init; }
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
    public ProviderNpmRef? Provider { get; init; }
}

public sealed record ProviderModelConfigCost
{
    public double Input { get; init; }
    public double Output { get; init; }
    public double? CacheRead { get; init; }
    public double? CacheWrite { get; init; }
    public ProviderModelOver200KCost? ContextOver200K { get; init; }
}

public sealed record ProviderModelConfigLimit
{
    public long Context { get; init; }
    public long Output { get; init; }
}

public sealed record ProviderOptionsConfig
{
    public string? ApiKey { get; init; }
    public string? BaseURL { get; init; }
    public string? EnterpriseUrl { get; init; }
    public bool? SetCacheKey { get; init; }
    public JsonElement? Timeout { get; init; }
}

public sealed record PermissionConfig
{
    public string? Edit { get; init; }
    public JsonElement? Bash { get; init; }
    public string? Webfetch { get; init; }
    public string? DoomLoop { get; init; }
    public string? ExternalDirectory { get; init; }
}

public sealed record EnterpriseConfig
{
    public string? Url { get; init; }
}

public sealed record ExperimentalConfig
{
    public ExperimentalHookConfig? Hook { get; init; }
    public long? ChatMaxRetries { get; init; }
    public bool? DisablePasteSummary { get; init; }
    public bool? BatchTool { get; init; }
    public bool? OpenTelemetry { get; init; }
    public IReadOnlyList<string>? PrimaryTools { get; init; }
}

public sealed record ExperimentalHookConfig
{
    public IReadOnlyDictionary<string, IReadOnlyList<HookCommand>>? FileEdited { get; init; }
    public IReadOnlyList<HookCommand>? SessionCompleted { get; init; }
}

public sealed record HookCommand
{
    public required IReadOnlyList<string> Command { get; init; }
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
}
