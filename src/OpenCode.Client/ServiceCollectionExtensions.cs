using Microsoft.Extensions.DependencyInjection;
using OpenCode.Client.Services;

namespace OpenCode.Client;

/// <summary>
/// Extension methods for registering OpenCode client services in the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers OpenCodeClient and all sub-services in the service collection.
    /// OpenCodeClient is registered as a singleton (owning the HttpClient lifetime).
    /// Integrates with <see cref="IHttpClientFactory"/> when available — call
    /// <c>services.AddHttpClient&lt;OpenCodeClient&gt;()</c> before calling this method
    /// to get connection pooling, DNS refresh, and other IHttpClientFactory benefits.
    /// </summary>
    public static IServiceCollection AddOpenCodeClient(
        this IServiceCollection services,
        Action<OpenCodeClientOptions> configure)
    {
        var options = new OpenCodeClientOptions();
        configure(options);

        // Register options
        services.AddSingleton(options);

        return services.AddOpenCodeClient();
    }

    /// <summary>
    /// Registers OpenCodeClient (resolving <see cref="OpenCodeClientOptions"/> from the container)
    /// and all sub-services. Uses <see cref="IHttpClientFactory"/> when registered, otherwise
    /// falls back to a plain <see cref="HttpClient"/>.
    /// </summary>
    public static IServiceCollection AddOpenCodeClient(this IServiceCollection services)
    {
        // Register OpenCodeClient as a singleton, resolving HttpClient via IHttpClientFactory
        // when available (for socket pooling, DNS refresh, etc.).
        services.AddSingleton<OpenCodeClient>(sp =>
        {
            var opts = sp.GetRequiredService<OpenCodeClientOptions>();
            var httpFactory = sp.GetService<IHttpClientFactory>();
            var http = httpFactory?.CreateClient(nameof(OpenCodeClient)) ?? new HttpClient();
            return new OpenCodeClient(() => http, opts);
        });

        // Register all service interfaces resolved through the client
        services.AddSingleton<IGlobalService>(sp => sp.GetRequiredService<OpenCodeClient>().Global);
        services.AddSingleton<IProjectService>(sp => sp.GetRequiredService<OpenCodeClient>().Projects);
        services.AddSingleton<IPathService>(sp => sp.GetRequiredService<OpenCodeClient>().Paths);
        services.AddSingleton<IVcsService>(sp => sp.GetRequiredService<OpenCodeClient>().Vcs);
        services.AddSingleton<IInstanceService>(sp => sp.GetRequiredService<OpenCodeClient>().Instance);
        services.AddSingleton<IConfigService>(sp => sp.GetRequiredService<OpenCodeClient>().Config);
        services.AddSingleton<IProviderService>(sp => sp.GetRequiredService<OpenCodeClient>().Providers);
        services.AddSingleton<ISessionService>(sp => sp.GetRequiredService<OpenCodeClient>().Sessions);
        services.AddSingleton<ICommandService>(sp => sp.GetRequiredService<OpenCodeClient>().Commands);
        services.AddSingleton<IFileService>(sp => sp.GetRequiredService<OpenCodeClient>().Files);
        services.AddSingleton<IFindService>(sp => sp.GetRequiredService<OpenCodeClient>().Find);
        services.AddSingleton<IToolService>(sp => sp.GetRequiredService<OpenCodeClient>().Tools);
        services.AddSingleton<IAgentService>(sp => sp.GetRequiredService<OpenCodeClient>().Agents);
        services.AddSingleton<IAuthService>(sp => sp.GetRequiredService<OpenCodeClient>().Auth);
        services.AddSingleton<IMcpService>(sp => sp.GetRequiredService<OpenCodeClient>().Mcp);
        services.AddSingleton<ILspService>(sp => sp.GetRequiredService<OpenCodeClient>().Lsp);
        services.AddSingleton<IFormatterService>(sp => sp.GetRequiredService<OpenCodeClient>().Formatters);
        services.AddSingleton<ILogService>(sp => sp.GetRequiredService<OpenCodeClient>().Log);
        services.AddSingleton<ITuiService>(sp => sp.GetRequiredService<OpenCodeClient>().Tui);
        services.AddSingleton<IPtyService>(sp => sp.GetRequiredService<OpenCodeClient>().Pty);
        services.AddSingleton<IEventService>(sp => sp.GetRequiredService<OpenCodeClient>().Events);

        return services;
    }
}
