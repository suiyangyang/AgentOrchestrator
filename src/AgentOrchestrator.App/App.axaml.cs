using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
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
        services.AddSingleton<ITaskGraphStore, JsonTaskGraphStore>();
        services.AddSingleton<ITaskGraphDirectParser, TaskGraphDirectParser>();
        services.AddSingleton<IDocumentReader, DocumentReader>();
        services.AddSingleton<JsonPlanningParser>();
        services.AddSingleton<ITaskGraphPlanner, LlmTaskGraphPlanner>();
        services.AddSingleton<INodeOutputInjector, DefaultNodeOutputInjector>();
        services.AddSingleton<ITaskGraphRuntimeHub, TaskGraphRuntimeHub>();
        services.AddSingleton<ITaskGraphExecutor, TaskGraphExecutor>();

        // ── ViewModels ──
        services.AddSingleton<SidebarViewModel>();
        services.AddSingleton<ChatWorkspaceViewModel>();
        services.AddSingleton<TaskGraphWorkspaceViewModel>();
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

    public static StartupOptions Parse(string[] args)
    {
        if (args is null || args.Length == 0)
        {
            return new StartupOptions();
        }

        string? openGraph = null;
        var maximize = false;

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
            }
        }

        return new StartupOptions { OpenGraphToken = openGraph, MaximizeGraph = maximize };
    }
}
