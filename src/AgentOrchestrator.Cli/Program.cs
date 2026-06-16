using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOrchestrator.App.Models.Chat;
using AgentOrchestrator.App.Models.Sidebar;
using AgentOrchestrator.App.Services.Agent;
using AgentOrchestrator.App.Services.Sidebar;
using OpenCode.Client;
using AgentChatRequest = AgentOrchestrator.App.Services.Agent.ChatRequest;

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
/// Commands:
///   health                            -- ping OpenCode
///   list-sessions                     -- list Agent sessions
///   new-session &lt;workdir&gt; [title]   -- create a new session
///   send &lt;agentSessionId&gt; &lt;prompt&gt; -- send a message and stream the response
///   messages &lt;agentSessionId&gt;       -- print message history
///   sidebar-list                      -- dump the local sidebar tree
///   verify &lt;workdir&gt; &lt;prompt&gt;     -- full smoke: new session + send + load messages
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        var settings = LoadSettings();
        var client = new OpenCodeClient(new OpenCodeClientOptions
        {
            BaseUrl = new Uri($"http://{settings.Host}:{settings.Port}"),
            Auth = new OpenCodeAuth { Username = settings.Username, Password = settings.Password },
        });
        await using var gateway = new OpenCodeAgentGateway(client, ownsClient: true);

        try
        {
            switch (args[0])
            {
                case "health":
                    return await HealthAsync(gateway);
                case "list-sessions":
                    return await ListSessionsAsync(gateway);
                case "new-session":
                    if (args.Length < 2)
                    {
                        Console.Error.WriteLine("usage: new-session <workdir> [title]");
                        return 2;
                    }
                    return await NewSessionAsync(gateway, args[1], args.Length >= 3 ? args[2] : null);
                case "send":
                    if (args.Length < 3)
                    {
                        Console.Error.WriteLine("usage: send <agentSessionId> <prompt>");
                        return 2;
                    }
                    return await SendAsync(gateway, args[1], args[2]);
                case "messages":
                    if (args.Length < 2)
                    {
                        Console.Error.WriteLine("usage: messages <agentSessionId>");
                        return 2;
                    }
                    return await MessagesAsync(gateway, args[1]);
                case "sidebar-list":
                    return await SidebarListAsync();
                case "verify":
                    if (args.Length < 3)
                    {
                        Console.Error.WriteLine("usage: verify <workdir> <prompt>");
                        return 2;
                    }
                    return await VerifyAsync(gateway, args[1], args[2]);
                default:
                    Console.Error.WriteLine($"unknown command: {args[0]}");
                    PrintUsage();
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
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
                using var doc = System.Text.Json.JsonDocument.Parse(json);
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

    // ── Commands ────────────────────────────────────────────────────────

    private static async Task<int> HealthAsync(IAgentGateway gateway)
    {
        var sessions = await gateway.ListSessionsAsync();
        Console.WriteLine($"OK: gateway '{gateway.AgentKind}' reachable, {sessions.Count} remote session(s).");
        return 0;
    }

    private static async Task<int> ListSessionsAsync(IAgentGateway gateway)
    {
        var sessions = await gateway.ListSessionsAsync();
        Console.WriteLine($"count: {sessions.Count}");
        foreach (var s in sessions)
        {
            Console.WriteLine($"- {s.AgentSessionId}  {s.Title}  created={s.CreatedAt}");
        }
        return 0;
    }

    private static async Task<int> NewSessionAsync(IAgentGateway gateway, string workdir, string? title)
    {
        var id = await gateway.CreateSessionAsync(new SessionCreateRequest(workdir, title));
        Console.WriteLine(id);
        return 0;
    }

    private static async Task<int> SendAsync(IAgentGateway gateway, string agentSessionId, string prompt)
    {
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
        return sawStream ? 0 : 3;
    }

    private static async Task<int> MessagesAsync(IAgentGateway gateway, string agentSessionId)
    {
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
        return 0;
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
        return 0;
    }

    private static async Task<int> VerifyAsync(IAgentGateway gateway, string workdir, string prompt)
    {
        var repo = new SqliteSidebarRepository();
        await repo.InitializeAsync();

        // 1. Create session.
        var project = await FindOrCreateProject(repo, workdir);
        var agentId = await gateway.CreateSessionAsync(new SessionCreateRequest(workdir, null));
        Console.WriteLine($"[1] new session: {agentId}");

        var record = new SessionRecord(
            SessionId: Guid.NewGuid().ToString("N"),
            AgentSessionId: agentId,
            Title: "新对话",
            ProjectId: project?.Id,
            CreatedAt: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
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

        return 0;
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
        Console.WriteLine("AgentOrchestrator.Cli <command> [args]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  health");
        Console.WriteLine("  list-sessions");
        Console.WriteLine("  new-session <workdir> [title]");
        Console.WriteLine("  send <agentSessionId> <prompt>");
        Console.WriteLine("  messages <agentSessionId>");
        Console.WriteLine("  sidebar-list");
        Console.WriteLine("  verify <workdir> <prompt>");
    }

    private sealed class CliSettings
    {
        public string Host { get; set; } = "0.0.0.0";
        public int Port { get; set; } = 8908;
        public string Username { get; set; } = "opencode";
        public string Password { get; set; } = "";
    }
}
