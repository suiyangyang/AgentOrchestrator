using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using AgentOrchestrator.App.Services.Agent;
using AgentOrchestrator.App.Services.Settings;
using AgentOrchestrator.App.Services.Sidebar;
using AgentOrchestrator.App.Services.TaskGraph;
using AgentOrchestrator.App.ViewModels;
using AgentOrchestrator.App.Views;
using Microsoft.Extensions.DependencyInjection;
using OpenCode.Client;

namespace AgentOrchestrator.App;

public partial class App : Application
{
    /// <summary>
    /// Parsed startup arguments. Drives auto-open behavior such as
    /// <c>--open-graph &lt;id-or-name-or-index&gt;</c> for screenshot tests.
    /// </summary>
    public StartupOptions StartupOptions { get; } = StartupOptions.Parse(Program.StartupArgs);

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            var provider = services.BuildServiceProvider();

            desktop.MainWindow = provider.GetRequiredService<MainWindow>();

            // Load appearance settings and populate DynamicResource tokens.
            var settings = new JsonAppSettingsService().Load();
            Application.Current.Resources["UiFontFamily"] = ResolveFontFamily(settings.UiFontFamily);
            Application.Current.Resources["UiFontSize"] = settings.UiFontSize;
            Application.Current.Resources["CodeFontFamily"] = ResolveFontFamily(settings.CodeFontFamily);
            Application.Current.Resources["CodeFontSize"] = settings.CodeFontSize;

            // Hand the parsed startup options to the shell so it can drive auto-open.
            var shell = provider.GetRequiredService<MainWindowViewModel>();
            shell.StartupOptions = StartupOptions;

            // ── Seed built-in templates + migrate old TaskTemplate files ──
            _ = Task.Run(async () =>
            {
                try
                {
                    var seeder = provider.GetRequiredService<BuiltInTemplateSeeder>();
                    await seeder.SeedIfMissingAsync().ConfigureAwait(false);
                    var migrator = provider.GetRequiredService<TaskTemplateMigrationService>();
                    await migrator.MigrateAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Seeding/migration failure must not block the UI.
                }
            });

            if (StartupOptions.PerfTest)
            {
                _ = RunPerfTestAsync(desktop, shell, StartupOptions.OpenGraphToken);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static FontFamily ResolveFontFamily(string name)
    {
        // Map known friendly names to embedded avares:// resources. Anything else
        // is treated as a system font name (e.g. "Consolas", "Inter").
        return name switch
        {
            "Source Han Sans" => EmbeddedSourceHanSans,
            _ => string.IsNullOrWhiteSpace(name) ? FontFamily.Default : new FontFamily(name),
        };
    }

    private static readonly FontFamily EmbeddedSourceHanSans =
        new("avares://AgentOrchestrator.App/Assets/Fonts/SourceHanSans-VF.otf#Source Han Sans");

    /// <summary>
    /// Drives the same code path the user exercises when they click a
    /// task graph card in the sidebar (load + activate) and when they
    /// click a node on the canvas (select). Reports the wall-clock cost
    /// of each step so a regression in the visual sync is visible as a
    /// single number. Default graph when no token is supplied: the
    /// 100-node "压力测试 100 节点" graph used by the regression check.
    /// </summary>
    private static async Task RunPerfTestAsync(IClassicDesktopStyleApplicationLifetime desktop, MainWindowViewModel shell, string? graphToken)
    {
        try
        {
            // Let the sidebar + view-model initialize (same window the user
            // would see on first launch). 3 s covers the cold path on the
            // dev box without burning CI minutes.
            await Task.Delay(3000).ConfigureAwait(true);

            var token = string.IsNullOrWhiteSpace(graphToken) ? "压力测试 100 节点" : graphToken;

            // Open the graph (same code path as clicking a sidebar card).
            var swOpen = System.Diagnostics.Stopwatch.StartNew();
            await shell.OpenTaskGraphByTokenAsync(token, maximize: false).ConfigureAwait(true);
            swOpen.Stop();

            // Let the dispatcher drain any post-open surface refresh
            // before we time the select-node step.
            await Task.Delay(500).ConfigureAwait(true);

            // Pick the first node in the graph and run SelectNodeCommand
            // — the exact entry point the canvas click handler invokes.
            // Force the dispatcher to run on the UI thread so we measure
            // the real cost of the visual sync, not the queue latency.
            int nodeCount = -1;
            long selectMs = -1;
            long selectDrainMs = -1;
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var graph = shell.TaskGraph.CurrentGraph;
                if (graph is null || graph.Nodes.Count == 0)
                {
                    return;
                }

                nodeCount = graph.Nodes.Count;
                var firstNode = graph.Nodes[0];

                // Pre-reset selection state so the first node really
                // changes (the second click on the same node would
                // short-circuit SetProperty, which would not exercise
                // the worst case).
                shell.TaskGraph.SelectNodeCommand.Execute(null!);
                await Task.Yield();

                var sw = System.Diagnostics.Stopwatch.StartNew();
                shell.TaskGraph.SelectNodeCommand.Execute(firstNode);
                sw.Stop();
                selectMs = sw.ElapsedMilliseconds;

                // Yield enough times for CollectionChanged handlers +
                // invalidate-measure passes to drain.
                sw.Restart();
                for (var i = 0; i < 20; i++) await Task.Yield();
                sw.Stop();
                selectDrainMs = sw.ElapsedMilliseconds;
            });

            Console.WriteLine($"PERF_OPEN_MS: {swOpen.ElapsedMilliseconds}");
            Console.WriteLine($"PERF_SELECT_MS: {selectMs} (sync wallclock, nodes={nodeCount})");
            Console.WriteLine($"PERF_SELECT_DRAIN_MS: {selectDrainMs} (post-yield drain, nodes={nodeCount})");
            desktop.Shutdown(0);

            Console.WriteLine($"PERF_OPEN_MS: {swOpen.ElapsedMilliseconds}");
            Console.WriteLine($"PERF_SELECT_MS: {selectMs} (sync wallclock, nodes={nodeCount})");
            Console.WriteLine($"PERF_SELECT_DRAIN_MS: {selectDrainMs} (post-yield drain, nodes={nodeCount})");
            desktop.Shutdown(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"PERF_RESULT_MS: -1 (error: {ex.GetType().Name}: {ex.Message})");
            desktop.Shutdown(1);
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // ── Settings ──
        services.AddSingleton<IAppSettingsService, JsonAppSettingsService>();

        // ── Agent (OpenCode) ──
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IAppSettingsService>().Load();
            return new OpenCodeClient(new OpenCodeClientOptions
            {
                BaseUrl = new Uri($"http://{settings.Host}:{settings.Port}"),
                Auth = string.IsNullOrEmpty(settings.Password)
                    ? null
                    : new OpenCodeAuth { Username = settings.Username, Password = settings.Password },
            });
        });
        services.AddSingleton(sp =>
        {
            var client = sp.GetRequiredService<OpenCodeClient>();
            return new OpenCodeAgentGateway(client, ownsClient: false);
        });
        services.AddSingleton<IAgentGateway>(sp => sp.GetRequiredService<OpenCodeAgentGateway>());

        // ── Sidebar / local persistence ──
        services.AddSingleton<ISidebarRepository, SqliteSidebarRepository>();
        services.AddSingleton<ITaskGraphTemplateInstantiator, TaskGraphTemplateInstantiator>();
        services.AddSingleton<ITaskGraphStore>(sp => new JsonTaskGraphStore(
            Path.Combine(DataPathProvider.DataDirectory, "TaskGraphs"),
            sp.GetRequiredService<ITaskGraphTemplateInstantiator>()));
        services.AddSingleton<ITaskGraphDirectParser, TaskGraphDirectParser>();
        services.AddSingleton<IDocumentReader, DocumentReader>();
        services.AddSingleton<JsonPlanningParser>();
        services.AddSingleton<ITaskGraphPlanner, LlmTaskGraphPlanner>();
        services.AddSingleton<INodeOutputInjector, DefaultNodeOutputInjector>();
        services.AddSingleton<ITaskGraphRuntimeHub, TaskGraphRuntimeHub>();
        services.AddSingleton<ITaskGraphExecutor, TaskGraphExecutor>();
        services.AddSingleton<ITaskGraphExecutionController>(sp => (ITaskGraphExecutionController)sp.GetRequiredService<ITaskGraphExecutor>());

        // ── Built-in template seeder + old-template migrator ──
        services.AddSingleton<BuiltInTemplateSeeder>();
        services.AddSingleton<TaskTemplateMigrationService>(sp => new TaskTemplateMigrationService(
            sp.GetRequiredService<ITaskGraphStore>()));

        // ── ViewModels ──
        services.AddSingleton<SidebarViewModel>();
        services.AddSingleton<ChatWorkspaceViewModel>(sp => new ChatWorkspaceViewModel(
            sp.GetRequiredService<IAgentGateway>(),
            sp.GetRequiredService<ISidebarRepository>(),
            sp.GetRequiredService<SidebarViewModel>(),
            sp.GetRequiredService<ITaskGraphStore>(),
            sp.GetRequiredService<ITaskGraphExecutionController>(),
            sp.GetRequiredService<ITaskGraphRuntimeHub>()));
        services.AddSingleton<TaskGraphWorkspaceViewModel>();
        services.AddSingleton<TaskGraphDocumentEditorViewModel>(sp => new TaskGraphDocumentEditorViewModel(
            sp.GetRequiredService<ITaskGraphStore>(),
            sp.GetRequiredService<SidebarViewModel>()));
        services.AddSingleton<TaskOrchestrationWorkspaceViewModel>(sp => new TaskOrchestrationWorkspaceViewModel(
            sp.GetRequiredService<ITaskGraphStore>(),
            sp.GetRequiredService<TaskGraphWorkspaceViewModel>(),
            sp.GetRequiredService<TaskGraphDocumentEditorViewModel>()));
        services.AddTransient<TaskGraphNodeDetailViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<SettingsViewModel>();

        // ── Views ──
        services.AddTransient<MainWindow>();
        services.AddTransient<SettingsWindow>();
        services.AddTransient<TaskGraphNodeDetailWindow>();
    }
}

/// <summary>
/// Parsed startup command-line options. Forwarded to <c>MainWindowViewModel</c>
/// so that screenshot / smoke tests can drive the app non-interactively.
/// </summary>
public sealed class StartupOptions
{
    /// <summary>Optional token referencing a saved task graph. Resolved by id,
    /// 1-based index, or name (in that order) — same lookup order used by the
    /// <c>taskgraph select</c> CLI command.</summary>
    public string? OpenGraphToken { get; init; }

    /// <summary>When true, the graph workspace is auto-maximized after the
    /// graph is loaded (i.e. sidebars are hidden, graph fills the window).</summary>
    public bool MaximizeGraph { get; init; }

    /// <summary>When true, after the main window + sidebar finish loading
    /// the shell resolves <see cref="OpenGraphToken"/> (or the default
    /// 100-node "压力测试 100 节点" graph when no token is supplied),
    /// drives the same code path that runs when the user clicks a task
    /// graph card / a graph node, and writes the elapsed wall-clock time
    /// for each step to stdout as <c>PERF_OPEN_MS: &lt;ms&gt;</c> /
    /// <c>PERF_SELECT_MS: &lt;ms&gt;</c>. Exits 0 on success / 1 on any
    /// exception. Use this to verify the click-flood + bulk-refresh
    /// fixes: graphs that used to freeze the UI for many seconds should
    /// complete in well under one second.</summary>
    public bool PerfTest { get; init; }

    /// <summary>When true, the shell opens the task orchestration
    /// independent workspace on startup (instead of the default chat
    /// workspace). Driven by the <c>orchestration open</c> CLI command.</summary>
    public bool OpenOrchestration { get; init; }

    public static StartupOptions Parse(string[] args)
    {
        if (args is null || args.Length == 0)
        {
            return new StartupOptions();
        }

        string? openGraph = null;
        var maximize = false;
        var perfTest = false;
        var openOrchestration = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--open-graph":
                case "--select-graph":
                case "--taskgraph":
                    if (i + 1 < args.Length)
                    {
                        openGraph = args[++i];
                    }
                    break;
                case "--maximize-graph":
                case "--fullscreen":
                    maximize = true;
                    break;
                case "--perf-test":
                    perfTest = true;
                    break;
                case "--open-orchestration":
                    openOrchestration = true;
                    break;
            }
        }

        return new StartupOptions { OpenGraphToken = openGraph, MaximizeGraph = maximize, PerfTest = perfTest, OpenOrchestration = openOrchestration };
    }
}
