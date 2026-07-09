using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Modules.ApiKeys;
using AutomateX.Web;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Engine:EgressAllowlist is a string[], which the env-var provider only binds via indexed keys
// (Engine__EgressAllowlist__0…). Accept a friendly comma-separated EGRESS_ALLOWLIST and expand it into
// those keys so operators can set the whole allowlist in one env var; appsettings can still supply a
// JSON array directly.
if (builder.Configuration["EGRESS_ALLOWLIST"] is { Length: > 0 } egressAllowlist)
{
    builder.Configuration.AddInMemoryCollection(
        egressAllowlist
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select((entry, i) => new KeyValuePair<string, string?>($"Engine:EgressAllowlist:{i}", entry)));
}

var connectionString = builder.Configuration.GetConnectionString("automatex")
    ?? throw new InvalidOperationException("Connection string 'automatex' not found.");

builder.AddAutomateXEngine(connectionString);
builder.AddAutomateXAuth();
builder.Services.AddFastEndpoints();
builder.Services.AddAutomateXRateLimiting();
builder.AddAutomateXRealtime();

var app = builder.Build();

app.UseAutomateXAuth();
app.MapDefaultEndpoints();
app.UseRateLimiter();
app.UseFastEndpoints(config => config.Endpoints.RoutePrefix = "api");
app.MapHub<ExecutionEventsHub>("/hubs/executions");
app.MapHub<PluginLogsHub>("/hubs/plugin-logs");

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
    if (app.Configuration.GetValue("Database:MigrateOnStartup", defaultValue: true))
    {
        // Advisory-locked so N nodes booting at once don't race MigrateAsync (v5.0 m6).
        await dbContext.MigrateWithAdvisoryLockAsync(connectionString);
    }

    await WorkspaceBootstrap.EnsureDefaultAsync(dbContext);

    var authOptions = scope.ServiceProvider.GetRequiredService<IOptions<AuthOptions>>().Value;
    await ApiKeyBootstrap.EnsureAsync(dbContext, authOptions, app.Logger);

    if (app.Environment.IsDevelopment())
    {
        await DevDataSeeder.SeedAsync(dbContext);
    }
}

await app.RunAsync();
