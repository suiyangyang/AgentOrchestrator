using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using AgentOrchestrator.App.Services.Agent;
using AgentOrchestrator.App.Services.Settings;
using AgentOrchestrator.App.Services.Sidebar;
using AgentOrchestrator.App.ViewModels;
using AgentOrchestrator.App.Views;
using Microsoft.Extensions.DependencyInjection;
using OpenCode.Client;

namespace AgentOrchestrator.App;

public partial class App : Application
{
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
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // ── Settings ──
        services.AddSingleton<IAppSettingsService, JsonAppSettingsService>();

        // ── Agent (OpenCode) ──
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IAppSettingsService>().Load();
            var options = new OpenCodeClientOptions
            {
                BaseUrl = new Uri($"http://{settings.Host}:{settings.Port}"),
                Auth = string.IsNullOrEmpty(settings.Password)
                    ? null
                    : new OpenCodeAuth { Username = settings.Username, Password = settings.Password },
            };
            var client = new OpenCodeClient(options);
            return new OpenCodeAgentGateway(client, ownsClient: true);
        });
        services.AddSingleton<IAgentGateway>(sp => sp.GetRequiredService<OpenCodeAgentGateway>());

        // ── Sidebar / local persistence ──
        services.AddSingleton<ISidebarRepository, SqliteSidebarRepository>();

        // ── ViewModels ──
        services.AddSingleton<SidebarViewModel>();
        services.AddSingleton<ChatWorkspaceViewModel>();
        services.AddSingleton<TaskGraphWorkspaceViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<SettingsViewModel>();

        // ── Views ──
        services.AddTransient<MainWindow>();
        services.AddTransient<SettingsWindow>();
    }
}
