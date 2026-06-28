using AutomateX.Database;
using AutomateX.Engine.Actions;
using AutomateX.Engine.Events;
using AutomateX.Engine.Plugins;
using AutomateX.Engine.Security;
using AutomateX.Plugin.Sdk;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

namespace AutomateX.Engine;

// The single composition root for the engine, shared by Program.cs and the test
// fixture — config drift between app and tests is impossible by construction.
public static class AutomateXEngine
{
    public static TBuilder AddAutomateXEngine<TBuilder>(
        this TBuilder builder,
        string connectionString,
        Action<EngineOptions>? configureEngine = null)
        where TBuilder : IHostApplicationBuilder
    {
        builder.UseWolverine(opts =>
        {
            // Engine handlers live in this assembly regardless of host (app or tests).
            opts.ApplicationAssembly = typeof(AutomateXEngine).Assembly;

            // Single-node by design — matches the one-container hosting story (plan §10).
            opts.Durability.Mode = DurabilityMode.Solo;

            opts.PersistMessagesWithPostgresql(connectionString, "wolverine");
            opts.Policies.UseDurableLocalQueues();
            opts.UseEntityFrameworkCoreTransactions();
            opts.Policies.AutoApplyTransactions();
        });

        builder.Services.AddDbContextWithWolverineIntegration<AutomateXDbContext>(
            options => options.UseNpgsql(connectionString),
            "wolverine");
        // No EF-level retry: it forbids the explicit transactions Wolverine's middleware uses,
        // and resilience lives at the message level (durable queue + step retry ladder).
        builder.EnrichNpgsqlDbContext<AutomateXDbContext>(settings => settings.DisableRetry = true);

        builder.Services.AddHttpClient(ActionContextFactory.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AutomateX/2.0");
        }).ConfigurePrimaryHttpMessageHandler(GuardedHandler);

        // Triggers may long-poll (e.g. matrix.onMessage holds a sync request open for
        // ~30s). The standard resilience handler's total-request timeout + retries are
        // fatal to that, so this client strips them and lets the listener bound each
        // request itself.
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is experimental but the right tool for long-poll clients.
        builder.Services.AddHttpClient(ActionContextFactory.TriggerHttpClientName, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AutomateX/2.0");
        }).RemoveAllResilienceHandlers().ConfigurePrimaryHttpMessageHandler(GuardedHandler);
#pragma warning restore EXTEXP0001

        builder.Services.AddOptions<EngineOptions>().BindConfiguration(EngineOptions.SectionName);
        builder.Services.AddOptions<EncryptionOptions>().BindConfiguration(EncryptionOptions.SectionName);
        builder.Services.AddSingleton<SecretCipher>();
        builder.Services.AddSingleton<DataKeyCache>();
        builder.Services.AddSingleton<DataKeyService>();
        builder.Services.AddSingleton<TenantCipher>();
        builder.Services.AddSingleton<KeyRotationService>();
        if (configureEngine is not null)
        {
            builder.Services.PostConfigure(configureEngine);
        }

        builder.Services.AddScoped<Modules.Workspaces.WorkspaceAccess>();
        builder.Services.AddScoped<StepTestRunner>();

        builder.Services.AddScoped<Modules.Variables.VariableLoader>();
        builder.Services.AddScoped<Modules.State.IWorkflowStateStore, Modules.State.WorkflowStateStore>();
        builder.Services.AddSingleton<ActionContextFactory>();
        builder.Services.AddSingleton<PluginAssemblies>();
        builder.Services.AddSingleton<IActionSource, BuiltInActionSource>();
        builder.Services.AddSingleton<ActionRegistry>();
        builder.Services.AddSingleton<Triggers.TriggerRegistry>();
        builder.Services.AddSingleton<Connections.IConnectionTypeSource, Connections.BuiltInConnectionTypeSource>();
        builder.Services.AddSingleton<Connections.ConnectionTypeRegistry>();
        builder.Services.AddSingleton<Connections.OAuthClient>();
        builder.Services.AddSingleton<Connections.OAuthStateProtector>();
        builder.Services.AddSingleton<Connections.ConnectionResolver>();
        builder.Services.AddSingleton<EngineEventBus>();
        builder.Services.AddSingleton<PluginReloader>();
        builder.Services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<EngineOptions>>().Value;
            var hostDll = options.PluginHostPath
                ?? Path.Combine(AppContext.BaseDirectory, "pluginhost", "AutomateX.PluginHost.dll");
            return new Plugins.PluginProcessSupervisor(
                sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<ILoggerFactory>(), hostDll);
        });

        builder.Services.AddSingleton<Metrics.ExecutionMetrics>();
        builder.Services.AddSingleton<IEngineEventListener, Metrics.MetricsEventListener>();

        builder.Services.AddScoped<Modules.Audit.IAuditSink, Modules.Audit.AuditSink>();
        builder.Services.AddSingleton<IEngineEventListener, Modules.Audit.AuditEventListener>();

        builder.Services.AddHostedService<CronScheduler>();
        builder.Services.AddHostedService<Triggers.PluginTriggerHost>();
        builder.Services.AddHostedService<PluginWatcher>();
        builder.Services.AddHostedService<StuckExecutionSweeper>();
        builder.Services.AddHostedService<RetentionSweeper>();

        return builder;
    }

    // Primary handler for the action/trigger HTTP clients. When the SSRF guard is on, it only
    // connects to non-blocked addresses (rebinding-proof — gates the IP actually dialed). Off = a
    // plain handler with default behavior.
    private static HttpMessageHandler GuardedHandler(IServiceProvider services)
    {
        var handler = new SocketsHttpHandler();
        if (services.GetRequiredService<IOptions<EngineOptions>>().Value.BlockPrivateNetworkRequests)
        {
            handler.ConnectCallback = SsrfGuard.FilteringConnectCallback;
        }

        return handler;
    }
}
