using AutomateX.Database;
using AutomateX.Engine.Actions;
using AutomateX.Engine.Events;
using AutomateX.Engine.Plugins;
using Microsoft.EntityFrameworkCore;
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
        });

        builder.Services.AddOptions<EngineOptions>().BindConfiguration(EngineOptions.SectionName);
        if (configureEngine is not null)
        {
            builder.Services.PostConfigure(configureEngine);
        }

        builder.Services.AddSingleton<ActionContextFactory>();
        builder.Services.AddSingleton<PluginAssemblies>();
        builder.Services.AddSingleton<IActionSource, BuiltInActionSource>();
        builder.Services.AddSingleton<IActionSource, PluginActionSource>();
        builder.Services.AddSingleton<ActionRegistry>();
        builder.Services.AddSingleton<EngineEventBus>();

        builder.Services.AddHostedService<CronScheduler>();
        builder.Services.AddHostedService<StuckExecutionSweeper>();

        return builder;
    }
}
