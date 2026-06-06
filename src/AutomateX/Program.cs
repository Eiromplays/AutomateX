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

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseFastEndpoints(config => config.Endpoints.RoutePrefix = "api");
app.MapHub<ExecutionEventsHub>("/hubs/executions");

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
    await dbContext.Database.MigrateAsync();
    await DevDataSeeder.SeedAsync(dbContext);
}

await app.RunAsync();
