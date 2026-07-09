using AutomateX.Database;
using AutomateX.Engine.Actions;
using AutomateX.Engine.Events;
using AutomateX.Engine.Plugins;
using AutomateX.Engine.Security;
using AutomateX.Plugin.Protocol;
using AutomateX.Plugin.Sdk;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

namespace AutomateX.Engine;

// The single composition root for the engine, shared by Program.cs and the test
// fixture — config drift between app and tests is impossible by construction.
public static class AutomateXEngine
{
    // Shared Postgres queue that carries the execution lane across nodes in Balanced mode.
    private const string ExecutionQueue = "executions";

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

            // v5.0: cluster mode is configurable. Balanced (default) enables Wolverine leader election +
            // distributed durability agents so the engine runs on N nodes; Solo starts every agent on the
            // local node (dev/test/single-node). Read from config here — it's needed while UseWolverine is
            // configured, before EngineOptions is bound.
            var durabilityMode = ResolveDurabilityMode(builder.Configuration);
            opts.Durability.Mode = durabilityMode;

            if (durabilityMode == DurabilityMode.Solo)
            {
                // Single node: fast in-proc durable local queues, no cross-node distribution needed.
                opts.PersistMessagesWithPostgresql(connectionString, "wolverine");
            }
            else
            {
                // Multi-node: the Postgres transport is a shared, durable queue every node pulls from
                // competitively (FOR UPDATE SKIP LOCKED), so executions distribute across the cluster. This
                // call also activates the durable inbox/outbox, so it replaces PersistMessagesWithPostgresql.
                opts.UsePostgresqlPersistenceAndTransport(connectionString, "wolverine", transportSchema: "wolverine_queues")
                    .AutoProvision();

                // The whole execution lane — the initial run and every cascaded step message — rides the
                // shared queue, so any node can claim the next hop. Everything else stays on local queues.
                // Postgres queues are polled (no LISTEN/NOTIFY); the interval trades start latency for DB load.
                var pollInterval = builder.Configuration.GetValue<TimeSpan?>("Engine:ExecutionQueuePollInterval")
                    ?? TimeSpan.FromSeconds(1);
                opts.ListenToPostgresqlQueue(ExecutionQueue).PollingInterval(pollInterval);
                opts.PublishMessage<RunWorkflow>().ToPostgresqlQueue(ExecutionQueue);
                opts.PublishMessage<ExecuteStep>().ToPostgresqlQueue(ExecutionQueue);
                opts.PublishMessage<AdvanceExecution>().ToPostgresqlQueue(ExecutionQueue);
                opts.PublishMessage<ResumeExecution>().ToPostgresqlQueue(ExecutionQueue);
                opts.PublishMessage<RetryFromStep>().ToPostgresqlQueue(ExecutionQueue);
            }

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

        // The egress policy is parsed once from the guard flag + allowlist and shared by the guarded HTTP
        // clients and the in-proc actions. Malformed allowlist entries are logged here, not fatal.
        builder.Services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<EngineOptions>>().Value;
            var policy = new EgressPolicy(options.BlockPrivateNetworkRequests, options.EgressAllowlist);
            if (policy.InvalidEntries.Count > 0)
            {
                sp.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("AutomateX.Engine.Egress")
                    .LogWarning(
                        "Ignored malformed Engine:EgressAllowlist entries: {Entries}",
                        string.Join(", ", policy.InvalidEntries));
            }

            return policy;
        });
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
        builder.Services.AddScoped<Modules.ApiKeys.ApiKeyResolver>();
        builder.Services.AddSingleton<Modules.ApiKeys.ApiKeyPresence>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<StepTestRunner>();

        builder.Services.AddScoped<Modules.Variables.VariableLoader>();
        builder.Services.AddScoped<Modules.Mcp.McpWorkflowRunner>();
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
        // No-op by default; the Web layer overrides this with the SignalR fan-out (v4.9 m2).
        builder.Services.AddSingleton<Plugins.IPluginLogSink, Plugins.NullPluginLogSink>();
        builder.Services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<EngineOptions>>().Value;
            var hostDll = options.PluginHostPath
                ?? Path.Combine(AppContext.BaseDirectory, "pluginhost", "AutomateX.PluginHost.dll");
            return new Plugins.PluginProcessSupervisor(
                sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<ILoggerFactory>(), hostDll,
                options.PluginLimits, options.BlockPrivateNetworkRequests, options.EgressAllowlist,
                sp.GetRequiredService<Plugins.IPluginLogSink>());
        });
        builder.Services.AddHostedService<Plugins.PluginMemoryWatchdog>();

        builder.Services.AddSingleton<Metrics.ExecutionMetrics>();
        builder.Services.AddSingleton<IEngineEventListener, Metrics.MetricsEventListener>();

        builder.Services.AddScoped<Modules.Audit.IAuditSink, Modules.Audit.AuditSink>();
        builder.Services.AddSingleton<IEngineEventListener, Modules.Audit.AuditEventListener>();

        // Cluster-singleton background work. In Balanced (multi-node) these are Wolverine agents: leader
        // election runs each on exactly one node and re-homes it on node loss (each SingularAgent is its own
        // IAgentFamily, resolved from DI so constructor injection works). In Solo (single node) they run as
        // plain hosted services on the one node — the same SingularBackgroundAgent is an IHostedService, so
        // its loop starts directly without depending on Wolverine's agent assignment. Single-node behaviour
        // is unchanged, and there's no path where both mechanisms run (registered one way or the other).
        if (ResolveDurabilityMode(builder.Configuration) == DurabilityMode.Solo)
        {
            builder.Services.AddHostedService<CronScheduler>();
            builder.Services.AddHostedService<Triggers.PluginTriggerHost>();
            builder.Services.AddHostedService<StuckExecutionSweeper>();
            builder.Services.AddHostedService<RetentionSweeper>();
        }
        else
        {
            builder.Services.AddSingleton<Wolverine.Runtime.Agents.IAgentFamily, CronScheduler>();
            builder.Services.AddSingleton<Wolverine.Runtime.Agents.IAgentFamily, Triggers.PluginTriggerHost>();
            builder.Services.AddSingleton<Wolverine.Runtime.Agents.IAgentFamily, StuckExecutionSweeper>();
            builder.Services.AddSingleton<Wolverine.Runtime.Agents.IAgentFamily, RetentionSweeper>();
        }

        // Node-local: PluginWatcher (and PluginMemoryWatchdog above) supervise this node's own plugin-host
        // processes, so they must run on every node — always hosted services.
        builder.Services.AddHostedService<PluginWatcher>();

        return builder;
    }

    // Balanced (default) = multi-node: leader election + distributed durability agents. Solo = single node.
    // Unset falls back to Balanced; an unrecognised value fails loudly rather than silently picking a mode.
    internal static DurabilityMode ResolveDurabilityMode(IConfiguration configuration)
    {
        var configured = configuration["Engine:DurabilityMode"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return DurabilityMode.Balanced;
        }

        return Enum.TryParse<DurabilityMode>(configured, ignoreCase: true, out var mode)
            ? mode
            : throw new InvalidOperationException(
                $"Engine:DurabilityMode '{configured}' is not a valid Wolverine DurabilityMode (e.g. Balanced, Solo).");
    }

    // Primary handler for the action/trigger HTTP clients. When the SSRF guard is on, it only
    // connects to non-blocked addresses (rebinding-proof — gates the IP actually dialed). Off = a
    // plain handler with default behavior.
    private static HttpMessageHandler GuardedHandler(IServiceProvider services)
    {
        var handler = new SocketsHttpHandler();
        var policy = services.GetRequiredService<EgressPolicy>();
        if (policy.BlockPrivate)
        {
            handler.ConnectCallback = EgressGuard.MakeConnectCallback(policy);
        }

        return handler;
    }
}
