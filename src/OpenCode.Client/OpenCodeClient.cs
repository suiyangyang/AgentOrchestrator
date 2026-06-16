using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCode.Client.Internal.Json;
using OpenCode.Client.Models;
using OpenCode.Client.Services;

namespace OpenCode.Client;

/// <summary>
/// Typed .NET client for the OpenCode HTTP server API.
/// Every endpoint from the OpenCode JS SDK is exposed via sub-service properties.
/// </summary>
public sealed class OpenCodeClient : IAsyncDisposable
{
    private readonly HttpClient _http;
    private readonly OpenCodeClientOptions _options;
    private readonly OpenCodeJsonSerializerContext _json;
    private bool _disposed;

    private IGlobalService? _global;
    private IProjectService? _projects;
    private IPathService? _paths;
    private IVcsService? _vcs;
    private IInstanceService? _instance;
    private IConfigService? _config;
    private IProviderService? _providers;
    private ISessionService? _sessions;
    private ICommandService? _commands;
    private IFileService? _files;
    private IFindService? _find;
    private IToolService? _tools;
    private IAgentService? _agents;
    private IAuthService? _auth;
    private IMcpService? _mcp;
    private ILspService? _lsp;
    private IFormatterService? _formatters;
    private ILogService? _log;
    private ITuiService? _tui;
    private IPtyService? _pty;
    private IEventService? _events;

    /// <summary>
    /// Creates a new OpenCodeClient with the given options.
    /// </summary>
    public OpenCodeClient(OpenCodeClientOptions options)
        : this(() => new HttpClient(), options)
    {
    }

    /// <summary>
    /// Creates a new OpenCodeClient using a factory for HttpClient (for DI/IHttpClientFactory integration).
    /// </summary>
    public OpenCodeClient(Func<HttpClient> httpClientFactory)
        : this(httpClientFactory, new OpenCodeClientOptions())
    {
    }

    /// <summary>
    /// Creates a new OpenCodeClient with an HttpClient factory and explicit options.
    /// </summary>
    public OpenCodeClient(Func<HttpClient> httpClientFactory, OpenCodeClientOptions options)
    {
        _options = options;

        var http = httpClientFactory();

        // Detect if this is the default (unmanaged) factory by checking whether the
        // HttpClient was already externally configured. If BaseAddress is still null,
        // the factory is likely `() => new HttpClient()`.
        bool isUnmanaged = http.BaseAddress is null;

        http.BaseAddress = options.BaseUrl;
        http.Timeout = options.Timeout;

        // Apply default headers
        foreach (var kvp in options.DefaultHeaders)
        {
            http.DefaultRequestHeaders.TryAddWithoutValidation(kvp.Key, kvp.Value);
        }

        // Inject Basic Auth only when the factory is the default (unmanaged) and
        // auth credentials are provided. When a custom factory is used (e.g. DI via
        // IHttpClientFactory), the user is responsible for configuring auth.
        if (options.Auth is not null && isUnmanaged)
        {
            var credentials = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes($"{options.Auth.Username}:{options.Auth.Password}"));
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
        }

        _http = http;

        // Configure JSON options with custom converters
        var jsonOptions = options.JsonSerializerOptions is not null
            ? new JsonSerializerOptions(options.JsonSerializerOptions)
            : new JsonSerializerOptions(JsonSerializerDefaults.Web);

        jsonOptions.Converters.Add(new EventConverter());
        jsonOptions.Converters.Add(new MessageConverter());
        jsonOptions.Converters.Add(new PartConverter());
        jsonOptions.Converters.Add(new ToolStateConverter());
        jsonOptions.Converters.Add(new FilePartSourceConverter());
        jsonOptions.Converters.Add(new SessionStatusConverter());
        jsonOptions.Converters.Add(new McpStatusConverter());
        jsonOptions.Converters.Add(new PartInputConverter());
        jsonOptions.Converters.Add(new AuthConverter());
        jsonOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        jsonOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;

        _json = new OpenCodeJsonSerializerContext(jsonOptions);
    }

    /// <summary>
    /// Global events (SSE).
    /// </summary>
    public IGlobalService Global => _global ??= new GlobalService(_http, _options, _json);

    /// <summary>
    /// Project management.
    /// </summary>
    public IProjectService Projects => _projects ??= new ProjectService(_http, _options, _json);

    /// <summary>
    /// Path information.
    /// </summary>
    public IPathService Paths => _paths ??= new PathService(_http, _options, _json);

    /// <summary>
    /// VCS (version control) information.
    /// </summary>
    public IVcsService Vcs => _vcs ??= new VcsService(_http, _options, _json);

    /// <summary>
    /// Instance lifecycle.
    /// </summary>
    public IInstanceService Instance => _instance ??= new InstanceService(_http, _options, _json);

    /// <summary>
    /// Configuration management.
    /// </summary>
    public IConfigService Config => _config ??= new ConfigService(_http, _options, _json);

    /// <summary>
    /// Provider management and authentication.
    /// </summary>
    public IProviderService Providers => _providers ??= new ProviderService(_http, _options, _json);

    /// <summary>
    /// Session management and messaging.
    /// </summary>
    public ISessionService Sessions => _sessions ??= new SessionService(_http, _options, _json);

    /// <summary>
    /// Command listing.
    /// </summary>
    public ICommandService Commands => _commands ??= new CommandService(_http, _options, _json);

    /// <summary>
    /// File system operations.
    /// </summary>
    public IFileService Files => _files ??= new FileService(_http, _options, _json);

    /// <summary>
    /// Text/file/symbol search.
    /// </summary>
    public IFindService Find => _find ??= new FindService(_http, _options, _json);

    /// <summary>
    /// Tool management (experimental).
    /// </summary>
    public IToolService Tools => _tools ??= new ToolService(_http, _options, _json);

    /// <summary>
    /// Agent listing.
    /// </summary>
    public IAgentService Agents => _agents ??= new AgentService(_http, _options, _json);

    /// <summary>
    /// Authentication credential management.
    /// </summary>
    public IAuthService Auth => _auth ??= new AuthService(_http, _options, _json);

    /// <summary>
    /// MCP server management.
    /// </summary>
    public IMcpService Mcp => _mcp ??= new McpService(_http, _options, _json);

    /// <summary>
    /// LSP server status.
    /// </summary>
    public ILspService Lsp => _lsp ??= new LspService(_http, _options, _json);

    /// <summary>
    /// Formatter status.
    /// </summary>
    public IFormatterService Formatters => _formatters ??= new FormatterService(_http, _options, _json);

    /// <summary>
    /// Logging.
    /// </summary>
    public ILogService Log => _log ??= new LogService(_http, _options, _json);

    /// <summary>
    /// TUI control operations.
    /// </summary>
    public ITuiService Tui => _tui ??= new TuiService(_http, _options, _json);

    /// <summary>
    /// PTY (pseudo-terminal) management.
    /// </summary>
    public IPtyService Pty => _pty ??= new PtyService(_http, _options, _json);

    /// <summary>
    /// Session event stream (SSE).
    /// </summary>
    public IEventService Events => _events ??= new EventService(_http, _options, _json);

    /// <summary>
    /// Health check convenience method.
    /// </summary>
    public async Task<HealthResponse> HealthAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("/global/health", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var options = new JsonSerializerOptions(_json.Options)
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };
        return JsonSerializer.Deserialize<HealthResponse>(json, options)!;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _http.Dispose();
        await ValueTask.CompletedTask;
    }
}
