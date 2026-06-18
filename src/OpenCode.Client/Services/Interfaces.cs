using OpenCode.Client.Models;
using OpenCode.Client.Models.Events;
using OpenCode.Client.Requests;

namespace OpenCode.Client.Services;

public interface IGlobalService
{
    Task<IAsyncEnumerable<GlobalEvent>> EventAsync(CancellationToken ct = default);
}

public interface IProjectService
{
    Task<IReadOnlyList<Project>> ListAsync(string? directory = null, CancellationToken ct = default);
    Task<Project> CurrentAsync(string? directory = null, CancellationToken ct = default);
}

public interface IPathService
{
    Task<PathInfo> GetAsync(string? directory = null, CancellationToken ct = default);
}

public interface IVcsService
{
    Task<VcsInfo> GetAsync(string? directory = null, CancellationToken ct = default);
}

public interface IInstanceService
{
    Task<bool> DisposeAsync(CancellationToken ct = default);
}

public interface IConfigService
{
    Task<Config> GetAsync(string? directory = null, CancellationToken ct = default);
    Task<Config> UpdateAsync(Config config, string? directory = null, CancellationToken ct = default);
    Task<ConfigProvidersResponse> GetProvidersAsync(string? directory = null, CancellationToken ct = default);
}

public interface IProviderService
{
    Task<ProviderListResponse> ListAsync(string? directory = null, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, IReadOnlyList<ProviderAuthMethod>>> AuthMethodsAsync(string? directory = null, CancellationToken ct = default);
    Task<ProviderAuthAuthorization> OauthAuthorizeAsync(string id, long method, string? directory = null, CancellationToken ct = default);
    Task<bool> OauthCallbackAsync(string id, long method, string? code = null, string? directory = null, CancellationToken ct = default);
}

public interface ISessionService
{
    Task<IReadOnlyList<Session>> ListAsync(string? directory = null, CancellationToken ct = default);
    Task<Session> CreateAsync(SessionCreateRequest body, string? directory = null, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, SessionStatus>> StatusAsync(string? directory = null, CancellationToken ct = default);
    Task<Session> GetAsync(string id, string? directory = null, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, string? directory = null, CancellationToken ct = default);
    Task<Session> UpdateAsync(string id, SessionUpdateRequest body, string? directory = null, CancellationToken ct = default);
    Task<IReadOnlyList<Session>> ChildrenAsync(string id, string? directory = null, CancellationToken ct = default);
    Task<IReadOnlyList<Todo>> TodoAsync(string id, string? directory = null, CancellationToken ct = default);
    Task<bool> InitAsync(string id, SessionInitRequest body, string? directory = null, CancellationToken ct = default);
    Task<Session> ForkAsync(string id, SessionForkRequest? body = null, string? directory = null, CancellationToken ct = default);
    Task<bool> AbortAsync(string id, string? directory = null, CancellationToken ct = default);
    Task<Session> ShareAsync(string id, string? directory = null, CancellationToken ct = default);
    Task<Session> UnshareAsync(string id, string? directory = null, CancellationToken ct = default);
    Task<IReadOnlyList<FileDiff>> DiffAsync(string id, string? messageID = null, string? directory = null, CancellationToken ct = default);
    Task<bool> SummarizeAsync(string id, SessionSummarizeRequest body, string? directory = null, CancellationToken ct = default);
    Task<IReadOnlyList<MessageWithParts>> MessagesAsync(string id, long? limit = null, string? directory = null, CancellationToken ct = default);
    Task<MessageWithParts> PromptAsync(string id, SessionPromptRequest body, string? directory = null, CancellationToken ct = default);
    Task<MessageWithParts> GetMessageAsync(string id, string messageID, string? directory = null, CancellationToken ct = default);
    Task PromptAsyncAsync(string id, SessionPromptRequest body, string? directory = null, CancellationToken ct = default);
    Task<IReadOnlyList<QuestionRequest>> QuestionsAsync(string id, string? directory = null, CancellationToken ct = default);
    Task<bool> ReplyQuestionAsync(string id, string requestID, QuestionReplyRequest body, string? directory = null, CancellationToken ct = default);
    Task<MessageWithParts> CommandAsync(string id, SessionCommandRequest body, string? directory = null, CancellationToken ct = default);
    Task<MessageWithParts> ShellAsync(string id, SessionShellRequest body, string? directory = null, CancellationToken ct = default);
    Task<Session> RevertAsync(string id, SessionRevertRequest body, string? directory = null, CancellationToken ct = default);
    Task<Session> UnrevertAsync(string id, string? directory = null, CancellationToken ct = default);
    Task<bool> RespondToPermissionAsync(string id, string permissionID, PostSessionIdPermissionsPermissionIdRequest body, string? directory = null, CancellationToken ct = default);
}

public interface ICommandService
{
    Task<IReadOnlyList<Command>> ListAsync(string? directory = null, CancellationToken ct = default);
}

public interface IFileService
{
    Task<IReadOnlyList<FileNode>> ListAsync(string path, string? directory = null, CancellationToken ct = default);
    Task<FileContent> ReadAsync(string path, string? directory = null, CancellationToken ct = default);
    Task<IReadOnlyList<FileStatus>> StatusAsync(string? directory = null, CancellationToken ct = default);
}

public interface IFindService
{
    Task<IReadOnlyList<FindMatch>> TextAsync(string pattern, string? directory = null, CancellationToken ct = default);
    Task<IReadOnlyList<string>> FilesAsync(string query, string? directory = null, string? dirs = null, CancellationToken ct = default);
    Task<IReadOnlyList<Symbol>> SymbolsAsync(string query, string? directory = null, CancellationToken ct = default);
}

public interface IToolService
{
    Task<ToolIds> GetIdsAsync(string? directory = null, CancellationToken ct = default);
    Task<IReadOnlyList<ToolListItem>> ListAsync(string provider, string model, string? directory = null, CancellationToken ct = default);
}

public interface IAgentService
{
    Task<IReadOnlyList<Agent>> ListAsync(string? directory = null, CancellationToken ct = default);
}

public interface IAuthService
{
    Task<bool> SetAsync(string id, object body, string? directory = null, CancellationToken ct = default);
}

public interface IMcpService
{
    Task<IReadOnlyDictionary<string, McpStatus>> StatusAsync(string? directory = null, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, McpStatus>> AddAsync(McpAddRequest body, string? directory = null, CancellationToken ct = default);
    Task<bool> ConnectAsync(string name, string? directory = null, CancellationToken ct = default);
    Task<bool> DisconnectAsync(string name, string? directory = null, CancellationToken ct = default);
    Task<bool> RemoveAuthAsync(string name, string? directory = null, CancellationToken ct = default);
    Task<McpAuthStartResponse> StartAuthAsync(string name, string? directory = null, CancellationToken ct = default);
    Task<McpStatus> AuthCallbackAsync(string name, McpAuthCallbackRequest body, string? directory = null, CancellationToken ct = default);
    Task<McpStatus> AuthenticateAsync(string name, string? directory = null, CancellationToken ct = default);
}

public interface ILspService
{
    Task<IReadOnlyList<LspStatus>> StatusAsync(string? directory = null, CancellationToken ct = default);
}

public interface IFormatterService
{
    Task<IReadOnlyList<FormatterStatus>> StatusAsync(string? directory = null, CancellationToken ct = default);
}

public interface ILogService
{
    Task<bool> LogAsync(AppLogRequest body, string? directory = null, CancellationToken ct = default);
}

public interface ITuiService
{
    Task<bool> AppendPromptAsync(TuiAppendPromptRequest body, string? directory = null, CancellationToken ct = default);
    Task<bool> OpenHelpAsync(string? directory = null, CancellationToken ct = default);
    Task<bool> OpenSessionsAsync(string? directory = null, CancellationToken ct = default);
    Task<bool> OpenThemesAsync(string? directory = null, CancellationToken ct = default);
    Task<bool> OpenModelsAsync(string? directory = null, CancellationToken ct = default);
    Task<bool> SubmitPromptAsync(string? directory = null, CancellationToken ct = default);
    Task<bool> ClearPromptAsync(string? directory = null, CancellationToken ct = default);
    Task<bool> ExecuteCommandAsync(TuiExecuteCommandRequest body, string? directory = null, CancellationToken ct = default);
    Task<bool> ShowToastAsync(TuiShowToastRequest body, string? directory = null, CancellationToken ct = default);
    Task<bool> PublishAsync(TuiPublishRequest body, string? directory = null, CancellationToken ct = default);
    Task<TuiControlRequest> ControlNextAsync(string? directory = null, CancellationToken ct = default);
    Task<bool> ControlResponseAsync(TuiControlResponseRequest body, string? directory = null, CancellationToken ct = default);
}

public interface IPtyService
{
    Task<IReadOnlyList<Pty>> ListAsync(string? directory = null, CancellationToken ct = default);
    Task<Pty> CreateAsync(PtyCreateRequest body, string? directory = null, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, string? directory = null, CancellationToken ct = default);
    Task<Pty> GetAsync(string id, string? directory = null, CancellationToken ct = default);
    Task<Pty> UpdateAsync(string id, PtyUpdateRequest body, string? directory = null, CancellationToken ct = default);
    Task<bool> ConnectAsync(string id, string? directory = null, CancellationToken ct = default);
}

public interface IEventService
{
    Task<IAsyncEnumerable<GlobalEvent>> SubscribeAsync(string? directory = null, CancellationToken ct = default);
}
