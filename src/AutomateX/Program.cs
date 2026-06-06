using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Engine.Actions;
using AutomateX.Engine.Plugins;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("automatex")
    ?? throw new InvalidOperationException("Connection string 'automatex' not found.");

// Wolverine-integrated registration: codegen-friendly lifetimes + envelope (outbox) tables mapped
// into the EF model, so entity saves and outgoing messages commit atomically.
// Enrich layers Aspire's telemetry, health checks and retry strategy on top.
builder.Services.AddDbContextWithWolverineIntegration<AutomateXDbContext>(
    options => options.UseNpgsql(connectionString),
    "wolverine");
// No EF-level retry: it forbids the explicit transactions Wolverine's transactional middleware
// uses, and resilience lives at the message level anyway (durable queue + step retry ladder).
builder.EnrichNpgsqlDbContext<AutomateXDbContext>(settings => settings.DisableRetry = true);

builder.Host.UseWolverine(opts =>
{
    opts.PersistMessagesWithPostgresql(connectionString, "wolverine");
    opts.Policies.UseDurableLocalQueues();
    opts.UseEntityFrameworkCoreTransactions();
    opts.Policies.AutoApplyTransactions();
});

builder.Services.AddFastEndpoints();
builder.Services.AddHttpClient(ActionContextFactory.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("AutomateX/2.0");
});
builder.Services.AddSingleton<ActionContextFactory>();
builder.Services.AddSingleton<IActionSource, BuiltInActionSource>();
builder.Services.AddSingleton<IActionSource, PluginActionSource>();
builder.Services.AddSingleton<ActionRegistry>();
builder.Services.Configure<EngineOptions>(builder.Configuration.GetSection(EngineOptions.SectionName));
builder.Services.AddHostedService<CronScheduler>();
builder.Services.AddHostedService<StuckExecutionSweeper>();

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseFastEndpoints(config => config.Endpoints.RoutePrefix = "api");

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
    await dbContext.Database.MigrateAsync();
    await DevDataSeeder.SeedAsync(dbContext);
}

await app.RunAsync();
