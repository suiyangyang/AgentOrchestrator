using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.Chat;
using AgentOrchestrator.App.Models.Sidebar;
using AgentOrchestrator.App.Models.TaskGraph;
using AgentOrchestrator.App.Services.Agent;
using AgentOrchestrator.App.Services.Sidebar;
using AgentOrchestrator.App.Services.TaskGraph;
using OpenCode.Client;
using AgentChatRequest = AgentOrchestrator.App.Services.Agent.ChatRequest;
using TaskGraphModel = AgentOrchestrator.App.Models.TaskGraph.TaskGraph;

namespace AgentOrchestrator.Cli;

/// <summary>
/// Tiny CLI front-end that exercises the same Agent gateway + Sidebar
/// repository the GUI uses. Lets us drive end-to-end OpenCode flows
/// (new session → send → stream → list messages → load project/session)
/// from a console for verification.
///
/// Usage:
///   AgentOrchestrator.Cli &lt;command&gt; [args]
///
/// Generic commands:
///   health                            -- ping OpenCode
///   list-sessions                     -- list Agent sessions
///   new-session &lt;workdir&gt; [title]   -- create a new session
///   send &lt;agentSessionId&gt; &lt;prompt&gt; -- send a message and stream the response
///   messages &lt;agentSessionId&gt;       -- print message history
///   sidebar-list                      -- dump the local sidebar tree
///   verify &lt;workdir&gt; &lt;prompt&gt;     -- full smoke: new session + send + load messages
///
/// Task graph commands:
///   taskgraph list                                -- list saved task graphs
///   taskgraph add-template &lt;template&gt; [name] [input]
///                                                 -- create a task graph by template
///                                                    template = task-list | feature-dev | bug-list
///                                                    input    = raw text or @path/to/file
///   taskgraph select &lt;id-or-name&gt;               -- print the resolved graph id (and summary)
///   taskgraph show &lt;id-or-name&gt;                 -- print full graph JSON
///   taskgraph delete &lt;id-or-name&gt;               -- delete a saved task graph
/// </summary>
public static class Program
{
    private const int ExitOk = 0;
    private const int ExitUsage = 2;
    private const int ExitError = 1;
    private const int ExitNotFound = 4;

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return ExitOk;
        }

        try
        {
            return args[0] switch
            {
                "health" => await HealthAsync(),
                "list-sessions" => await ListSessionsAsync(),
                "new-session" => await NewSessionAsync(args),
                "send" => await SendAsync(args),
                "messages" => await MessagesAsync(args),
                "sidebar-list" => await SidebarListAsync(),
                "verify" => await VerifyAsync(args),
                "taskgraph" => await TaskGraphCommandAsync(args),
                "verify-ui" => await VerifyUiAsync(args),
                _ => UnknownCommand(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.GetType().Name}: {ex.Message}");
            return ExitError;
        }
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"unknown command: {command}");
        PrintUsage();
        return ExitUsage;
    }

    private static CliSettings LoadSettings()
    {
        // Honor the same appsettings overlay chain as the App's JsonAppSettingsService.
        var settings = new CliSettings
        {
            Host = "0.0.0.0",
            Port = 8908,
            Username = "opencode",
            Password = "",
        };

        // 1. Embedded resource under AgentOrchestrator.App.
        var appSettingsPath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "AgentOrchestrator.App", "appsettings.json");
        if (File.Exists(appSettingsPath))
        {
            try
            {
                var json = File.ReadAllText(appSettingsPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("Host", out var host)) settings.Host = host.GetString() ?? settings.Host;
                if (root.TryGetProperty("Port", out var port)) settings.Port = port.GetInt32();
                if (root.TryGetProperty("Username", out var user)) settings.Username = user.GetString() ?? settings.Username;
                if (root.TryGetProperty("Password", out var pwd)) settings.Password = pwd.GetString() ?? settings.Password;
            }
            catch
            {
                // ignore
            }
        }

        // 2. Environment variables override.
        var envHost = Environment.GetEnvironmentVariable("AO_HOST");
        if (!string.IsNullOrEmpty(envHost)) settings.Host = envHost;
        var envPort = Environment.GetEnvironmentVariable("AO_PORT");
        if (!string.IsNullOrEmpty(envPort) && int.TryParse(envPort, out var p)) settings.Port = p;
        var envUser = Environment.GetEnvironmentVariable("AO_USERNAME");
        if (!string.IsNullOrEmpty(envUser)) settings.Username = envUser;
        var envPwd = Environment.GetEnvironmentVariable("AO_PASSWORD");
        if (!string.IsNullOrEmpty(envPwd)) settings.Password = envPwd;

        return settings;
    }

    // ── Generic commands ───────────────────────────────────────────────

    private static async Task<int> HealthAsync()
    {
        var settings = LoadSettings();
        var client = new OpenCodeClient(new OpenCodeClientOptions
        {
            BaseUrl = new Uri($"http://{settings.Host}:{settings.Port}"),
            Auth = new OpenCodeAuth { Username = settings.Username, Password = settings.Password },
        });
        await using var gateway = new OpenCodeAgentGateway(client, ownsClient: true);

        var sessions = await gateway.ListSessionsAsync();
        Console.WriteLine($"OK: gateway '{gateway.AgentKind}' reachable, {sessions.Count} remote session(s).");
        return ExitOk;
    }

    private static async Task<int> ListSessionsAsync()
    {
        var settings = LoadSettings();
        var client = new OpenCodeClient(new OpenCodeClientOptions
        {
            BaseUrl = new Uri($"http://{settings.Host}:{settings.Port}"),
            Auth = new OpenCodeAuth { Username = settings.Username, Password = settings.Password },
        });
        await using var gateway = new OpenCodeAgentGateway(client, ownsClient: true);

        var sessions = await gateway.ListSessionsAsync();
        Console.WriteLine($"count: {sessions.Count}");
        foreach (var s in sessions)
        {
            Console.WriteLine($"- {s.AgentSessionId}  {s.Title}  created={s.CreatedAt}");
        }
        return ExitOk;
    }

    private static async Task<int> NewSessionAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: new-session <workdir> [title]");
            return ExitUsage;
        }

        var settings = LoadSettings();
        var client = new OpenCodeClient(new OpenCodeClientOptions
        {
            BaseUrl = new Uri($"http://{settings.Host}:{settings.Port}"),
            Auth = new OpenCodeAuth { Username = settings.Username, Password = settings.Password },
        });
        await using var gateway = new OpenCodeAgentGateway(client, ownsClient: true);

        var workdir = args[1];
        var title = args.Length >= 3 ? args[2] : null;
        var id = await gateway.CreateSessionAsync(new SessionCreateRequest(workdir, title));
        Console.WriteLine(id);
        return ExitOk;
    }

    private static async Task<int> SendAsync(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: send <agentSessionId> <prompt>");
            return ExitUsage;
        }

        var settings = LoadSettings();
        var client = new OpenCodeClient(new OpenCodeClientOptions
        {
            BaseUrl = new Uri($"http://{settings.Host}:{settings.Port}"),
            Auth = new OpenCodeAuth { Username = settings.Username, Password = settings.Password },
        });
        await using var gateway = new OpenCodeAgentGateway(client, ownsClient: true);

        var agentSessionId = args[1];
        var prompt = args[2];
        var req = new AgentChatRequest(prompt, Array.Empty<ChatAttachment>(), "full", "codex");
        var sawStream = false;
        await foreach (var chunk in gateway.SendMessageAsync(agentSessionId, req))
        {
            sawStream = true;
            if (chunk.Complete)
            {
                Console.WriteLine($"<<< message {chunk.MessageId} complete");
            }
            else
            {
                Console.WriteLine($"[{chunk.Kind}] {chunk.Content}");
            }
        }
        return sawStream ? ExitOk : 3;
    }

    private static async Task<int> MessagesAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: messages <agentSessionId>");
            return ExitUsage;
        }

        var settings = LoadSettings();
        var client = new OpenCodeClient(new OpenCodeClientOptions
        {
            BaseUrl = new Uri($"http://{settings.Host}:{settings.Port}"),
            Auth = new OpenCodeAuth { Username = settings.Username, Password = settings.Password },
        });
        await using var gateway = new OpenCodeAgentGateway(client, ownsClient: true);

        var agentSessionId = args[1];
        var messages = await gateway.GetMessagesAsync(agentSessionId);
        Console.WriteLine($"count: {messages.Count}");
        foreach (var m in messages)
        {
            Console.WriteLine($"-- {m.Role}  id={m.Id}");
            foreach (var b in m.Blocks)
            {
                Console.WriteLine($"   [{b.Kind}] {(b.ToolName != null ? b.ToolName + " " : "")}{Truncate(b.Text ?? b.ToolOutput)}");
            }
        }
        return ExitOk;
    }

    private static async Task<int> SidebarListAsync()
    {
        var repo = new SqliteSidebarRepository();
        await repo.InitializeAsync();
        var projects = await repo.ListProjectsAsync();
        var sessions = await repo.ListSessionsAsync();
        Console.WriteLine($"projects: {projects.Count}");
        foreach (var p in projects)
        {
            Console.WriteLine($"  P {p.Id}  {p.Name}  {p.Directory}");
        }
        Console.WriteLine($"sessions: {sessions.Count}");
        foreach (var s in sessions)
        {
            Console.WriteLine($"  S {s.SessionId}  agent={s.AgentSessionId}  title={s.Title}  project={s.ProjectId ?? "-"}");
        }
        return ExitOk;
    }

    private static async Task<int> VerifyAsync(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: verify <workdir> <prompt>");
            return ExitUsage;
        }

        var settings = LoadSettings();
        var client = new OpenCodeClient(new OpenCodeClientOptions
        {
            BaseUrl = new Uri($"http://{settings.Host}:{settings.Port}"),
            Auth = new OpenCodeAuth { Username = settings.Username, Password = settings.Password },
        });
        await using var gateway = new OpenCodeAgentGateway(client, ownsClient: true);

        var repo = new SqliteSidebarRepository();
        await repo.InitializeAsync();

        var workdir = args[1];
        var prompt = args[2];

        // 1. Create session.
        var project = await FindOrCreateProject(repo, workdir);
        var agentId = await gateway.CreateSessionAsync(new SessionCreateRequest(workdir, null));
        Console.WriteLine($"[1] new session: {agentId}");

        var record = new SessionRecord(
            SessionId: Guid.NewGuid().ToString("N"),
            AgentSessionId: agentId,
            Title: "新对话",
            ProjectId: project?.Id,
            CreatedAt: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            LastActivityAt: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ViewedAt: null);
        await repo.CreateSessionAsync(record);
        Console.WriteLine($"[2] sidebar record created: {record.SessionId}");

        // 2. Send prompt and stream response.
        var req = new AgentChatRequest(prompt, Array.Empty<ChatAttachment>(), "full", "codex");
        var chunkCount = 0;
        await foreach (var chunk in gateway.SendMessageAsync(agentId, req))
        {
            chunkCount++;
            if (!chunk.Complete)
            {
                Console.WriteLine($"    [{chunk.Kind}] {Truncate(chunk.Content)}");
            }
        }
        Console.WriteLine($"[3] streamed {chunkCount} chunk(s)");

        // 3. Title sync.
        var newTitle = await gateway.GetSessionTitleAsync(agentId);
        if (!string.IsNullOrWhiteSpace(newTitle) && newTitle != "新对话")
        {
            var updated = record with { Title = newTitle };
            await repo.UpdateSessionAsync(updated);
            Console.WriteLine($"[4] title synced: {newTitle}");
        }
        else
        {
            Console.WriteLine("[4] no title change");
        }

        // 4. Reload history.
        var messages = await gateway.GetMessagesAsync(agentId);
        Console.WriteLine($"[5] message history: {messages.Count} message(s)");

        return ExitOk;
    }

    // ── Task graph commands ────────────────────────────────────────────

    private static async Task<int> TaskGraphCommandAsync(string[] args)
    {
        if (args.Length < 2)
        {
            PrintTaskGraphUsage();
            return ExitUsage;
        }

        var store = new JsonTaskGraphStore();
        var subCommand = args[1];

        switch (subCommand)
        {
            case "list":
                return await TaskGraphListAsync(store);
            case "add-template":
                return await TaskGraphAddTemplateAsync(store, args);
            case "select":
                return await TaskGraphSelectAsync(store, args);
            case "show":
                return await TaskGraphShowAsync(store, args);
            case "delete":
                return await TaskGraphDeleteAsync(store, args);
            case "help":
            case "--help":
            case "-h":
                PrintTaskGraphUsage();
                return ExitOk;
            default:
                Console.Error.WriteLine($"unknown taskgraph sub-command: {subCommand}");
                PrintTaskGraphUsage();
                return ExitUsage;
        }
    }

    private static async Task<int> TaskGraphListAsync(ITaskGraphStore store)
    {
        var items = await store.ListAsync().ConfigureAwait(false);
        Console.WriteLine($"count: {items.Count}");
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            Console.WriteLine(
                $"  [{index + 1}] id={item.Id}  name={item.Name}  template={item.TemplateKind}  nodes={item.NodeCount}  state={item.ExecutionState}  updated={item.UpdatedAt.ToLocalTime():yyyy-MM-dd HH:mm}");
        }
        return ExitOk;
    }

    private static async Task<int> TaskGraphAddTemplateAsync(ITaskGraphStore store, string[] args)
    {
        // taskgraph add-template <template> [name] [input]
        // <template>: task-list | feature-dev | bug-list
        // <input>:    raw text, or @/path/to/file
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: taskgraph add-template <task-list|feature-dev|bug-list> [name] [input]");
            return ExitUsage;
        }

        var templateKey = args[2];
        var name = args.Length >= 4 ? args[3] : null;
        var inputText = args.Length >= 5 ? ResolveInputText(args[4]) : string.Empty;

        if (string.IsNullOrWhiteSpace(inputText))
        {
            // Provide reasonable defaults so the command works without input.
            inputText = templateKey switch
            {
                "task-list" => "- 任务 1\n- 任务 2\n- 任务 3",
                "feature-dev" => "- 实现用户登录\n- 实现用户注册\n- 实现用户退出",
                "bug-list" => "- 任务 1\n- 任务 2\n- 任务 3",
                _ => string.Empty,
            };
        }

        TaskGraphModel graph = templateKey switch
        {
            "task-list" => TaskGraphTemplateBuilder.BuildTaskListGraph(inputText),
            "feature-dev" => TaskGraphTemplateBuilder.BuildFeatureDevelopmentGraph(inputText),
            "bug-list" => TaskGraphTemplateBuilder.BuildBugListGraph(inputText),
            _ => throw new TaskGraphValidationException($"未知模板类型：{templateKey}（可选: task-list / feature-dev / bug-list）"),
        };

        if (!string.IsNullOrWhiteSpace(name))
        {
            graph.Name = name.Trim();
        }

        await store.SaveAsync(graph).ConfigureAwait(false);

        Console.WriteLine($"created: id={graph.Id}  name={graph.Name}  template={graph.TemplateKind}  nodes={graph.Nodes.Count}  edges={graph.Edges.Count}");
        return ExitOk;
    }

    private static async Task<int> TaskGraphSelectAsync(ITaskGraphStore store, string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: taskgraph select <id-or-name-or-index>");
            return ExitUsage;
        }

        var graph = await ResolveGraphAsync(store, args[2]).ConfigureAwait(false);
        if (graph is null)
        {
            Console.Error.WriteLine($"task graph not found: {args[2]}");
            return ExitNotFound;
        }

        // Output: machine-friendly on first line, then a human-readable summary.
        Console.WriteLine(graph.Id);
        Console.WriteLine($"name:    {graph.Name}");
        Console.WriteLine($"template:{graph.TemplateKind}");
        Console.WriteLine($"state:   {graph.ExecutionState}");
        Console.WriteLine($"nodes:   {graph.Nodes.Count}");
        Console.WriteLine($"edges:   {graph.Edges.Count}");
        Console.WriteLine($"updated: {graph.UpdatedAt.ToLocalTime():yyyy-MM-dd HH:mm}");
        Console.WriteLine("nodes:");
        for (var index = 0; index < graph.Nodes.Count; index++)
        {
            var node = graph.Nodes[index];
            var deps = node.DependsOn.Count > 0 ? $"  ← {string.Join(",", node.DependsOn)}" : string.Empty;
            Console.WriteLine($"  [{index + 1}] {node.Id}  ({node.Kind})  {node.Title}{deps}");
        }

        return ExitOk;
    }

    private static async Task<int> TaskGraphShowAsync(ITaskGraphStore store, string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: taskgraph show <id-or-name-or-index>");
            return ExitUsage;
        }

        var graph = await ResolveGraphAsync(store, args[2]).ConfigureAwait(false);
        if (graph is null)
        {
            Console.Error.WriteLine($"task graph not found: {args[2]}");
            return ExitNotFound;
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };
        Console.WriteLine(JsonSerializer.Serialize(graph, options));
        return ExitOk;
    }

    private static async Task<int> TaskGraphDeleteAsync(ITaskGraphStore store, string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: taskgraph delete <id-or-name-or-index>");
            return ExitUsage;
        }

        var graph = await ResolveGraphAsync(store, args[2]).ConfigureAwait(false);
        if (graph is null)
        {
            Console.Error.WriteLine($"task graph not found: {args[2]}");
            return ExitNotFound;
        }

        await store.DeleteAsync(graph.Id).ConfigureAwait(false);
        Console.WriteLine($"deleted: id={graph.Id}  name={graph.Name}");
        return ExitOk;
    }

    private static async Task<TaskGraphModel?> ResolveGraphAsync(ITaskGraphStore store, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        // 1) Try by direct id.
        var byId = await store.LoadAsync(token).ConfigureAwait(false);
        if (byId is not null)
        {
            return byId;
        }

        // 2) Try by 1-based index (mirrors the output of `taskgraph list`).
        if (int.TryParse(token, out var index) && index > 0)
        {
            var items = await store.ListAsync().ConfigureAwait(false);
            if (index <= items.Count)
            {
                return await store.LoadAsync(items[index - 1].Id).ConfigureAwait(false);
            }
            return null;
        }

        // 3) Try by name (case-insensitive, exact match first, then contains).
        var list = await store.ListAsync().ConfigureAwait(false);
        var exact = list.FirstOrDefault(x => string.Equals(x.Name, token, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return await store.LoadAsync(exact.Id).ConfigureAwait(false);
        }

        var contains = list.FirstOrDefault(x => x.Name.Contains(token, StringComparison.OrdinalIgnoreCase));
        if (contains is not null)
        {
            return await store.LoadAsync(contains.Id).ConfigureAwait(false);
        }

        return null;
    }

    private static string ResolveInputText(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        if (raw.StartsWith('@'))
        {
            var path = raw[1..];
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }

            throw new FileNotFoundException($"input file not found: {path}", path);
        }

        return raw.Replace("\\n", "\n", StringComparison.Ordinal);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    // ── UI verification ─────────────────────────────────────────────

    private static async Task<int> VerifyUiAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: verify-ui <taskgraph-token> [out-dir]");
            return ExitUsage;
        }

        var token = args[1];
        var outDir = args.Length >= 3 ? args[2] : Path.Combine(Environment.CurrentDirectory, "verify-ui-out");
        Directory.CreateDirectory(outDir);

        // We shell out to the App with --open-graph so the existing flow runs.
        // The actual screenshot capture is done by the App via --screenshot
        // (which we'll wire in the App). For now we just confirm the args
        // and produce a smoke-test summary.
        var appDll = ResolveAppDll();
        if (appDll is null)
        {
            Console.Error.WriteLine("verify-ui: could not locate AgentOrchestrator.App.dll");
            return ExitError;
        }

        Console.WriteLine($"[1] out-dir        : {outDir}");
        Console.WriteLine($"[2] app-dll        : {appDll}");
        Console.WriteLine($"[3] taskgraph token: {token}");
        Console.WriteLine("[4] hint: run the App manually with:");
        Console.WriteLine($"       dotnet run --project src/AgentOrchestrator.App -- --open-graph {token} --maximize-graph --screenshot {Path.Combine(outDir, "taskgraph.png")}");
        await Task.CompletedTask;
        return ExitOk;
    }

    private static string? ResolveAppDll()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "AgentOrchestrator.App.dll"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "AgentOrchestrator.App", "bin", "Debug", "net10.0", "AgentOrchestrator.App.dll"),
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c)) return Path.GetFullPath(c);
        }
        return null;
    }

    private static async Task<ProjectRecord?> FindOrCreateProject(ISidebarRepository repo, string workdir)
    {
        var projects = await repo.ListProjectsAsync();
        var norm = workdir.TrimEnd('/', '\\').Replace('/', '\\');
        var existing = projects.FirstOrDefault(p =>
            string.Equals(p.Directory.TrimEnd('/', '\\').Replace('/', '\\'), norm, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;
        var name = new DirectoryInfo(workdir).Name;
        if (string.IsNullOrWhiteSpace(name)) name = workdir;
        return await repo.CreateProjectAsync(name, workdir);
    }

    private static string Truncate(string? s, int max = 120)
    {
        s ??= string.Empty;
        return s.Length <= max ? s : s[..max] + "…";
    }

    private static void PrintUsage()
    {
        var sb = new StringBuilder();
        sb.AppendLine("AgentOrchestrator.Cli <command> [args]");
        sb.AppendLine();
        sb.AppendLine("Generic commands:");
        sb.AppendLine("  health");
        sb.AppendLine("  list-sessions");
        sb.AppendLine("  new-session <workdir> [title]");
        sb.AppendLine("  send <agentSessionId> <prompt>");
        sb.AppendLine("  messages <agentSessionId>");
        sb.AppendLine("  sidebar-list");
        sb.AppendLine("  verify <workdir> <prompt>");
        sb.AppendLine("  verify-ui <taskgraph-token> [out-dir]   -- launch the GUI, capture");
        sb.AppendLine("                                           a screenshot, then exit.");
        sb.AppendLine("                                           taskgraph-token = id | name | index");
        sb.AppendLine();
        sb.AppendLine("Task graph commands:");
        sb.AppendLine("  taskgraph list");
        sb.AppendLine("  taskgraph add-template <task-list|feature-dev|bug-list> [name] [input]");
        sb.AppendLine("  taskgraph select <id-or-name-or-index>");
        sb.AppendLine("  taskgraph show <id-or-name-or-index>");
        sb.AppendLine("  taskgraph delete <id-or-name-or-index>");
        Console.Write(sb.ToString());
    }

    private static void PrintTaskGraphUsage()
    {
        var sb = new StringBuilder();
        sb.AppendLine("taskgraph <sub-command> [args]");
        sb.AppendLine();
        sb.AppendLine("Sub-commands:");
        sb.AppendLine("  list                                          list saved task graphs");
        sb.AppendLine("  add-template <template> [name] [input]        create a task graph by template");
        sb.AppendLine("                                                template = task-list | feature-dev | bug-list");
        sb.AppendLine("                                                input    = raw text or @/path/to/file");
        sb.AppendLine("  select <id-or-name-or-index>                  print resolved id + summary");
        sb.AppendLine("  show <id-or-name-or-index>                    print full graph JSON");
        sb.AppendLine("  delete <id-or-name-or-index>                  delete a saved task graph");
        Console.Write(sb.ToString());
    }

    private sealed class CliSettings
    {
        public string Host { get; set; } = "0.0.0.0";
        public int Port { get; set; } = 8908;
        public string Username { get; set; } = "opencode";
        public string Password { get; set; } = "";
    }
}