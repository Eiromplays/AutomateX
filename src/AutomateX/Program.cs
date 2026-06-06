using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Engine.Actions;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Postgresql;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("automatex")
    ?? throw new InvalidOperationException("Connection string 'automatex' not found.");

// Explicit registration (not Aspire's opaque factory) so Wolverine's codegen can inject the context.
// Options must be singleton — Wolverine resolves singletons up front but refuses scoped lambda factories.
// Enrich layers Aspire's telemetry, health checks and retry strategy on top.
builder.Services.AddDbContext<AutomateXDbContext>(
    options => options.UseNpgsql(connectionString),
    optionsLifetime: ServiceLifetime.Singleton);
builder.EnrichNpgsqlDbContext<AutomateXDbContext>();

builder.Host.UseWolverine(opts =>
{
    opts.PersistMessagesWithPostgresql(connectionString, "wolverine");
    opts.Policies.UseDurableLocalQueues();
});

builder.Services.AddFastEndpoints();
builder.Services.AddHttpClient(HttpRequestAction.ClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("AutomateX/2.0");
});
builder.Services.AddSingleton<HttpRequestAction>();
builder.Services.AddSingleton<IActionExecutor, HttpRequestExecutor>();
builder.Services.AddSingleton<ActionRegistry>();
builder.Services.AddHostedService<CronScheduler>();

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
