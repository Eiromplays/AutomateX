using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Plugin.Sdk;
using AutomateX.Web;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("automatex")
    ?? throw new InvalidOperationException("Connection string 'automatex' not found.");

builder.AddAutomateXEngine(connectionString);
builder.Services.AddFastEndpoints();
builder.Services.AddSignalR();
builder.Services.AddSingleton<IEngineEventListener, SignalRExecutionEventListener>();
builder.Services.Configure<ApiKeyOptions>(builder.Configuration.GetSection(ApiKeyOptions.SectionName));

var app = builder.Build();

app.UseMiddleware<ApiKeyMiddleware>();
app.MapDefaultEndpoints();
app.UseFastEndpoints(config => config.Endpoints.RoutePrefix = "api");
app.MapHub<ExecutionEventsHub>("/hubs/executions");

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
    if (app.Configuration.GetValue("Database:MigrateOnStartup", defaultValue: true))
    {
        await dbContext.Database.MigrateAsync();
    }

    if (app.Environment.IsDevelopment())
    {
        await DevDataSeeder.SeedAsync(dbContext);
    }
}

await app.RunAsync();
