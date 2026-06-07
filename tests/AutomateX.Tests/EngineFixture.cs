using System.Collections.Concurrent;
using System.Security.Cryptography;
using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Engine.Actions;
using AutomateX.Modules.Workspaces;
using AutomateX.Plugin.Sdk;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Xunit;

namespace AutomateX.Tests;

// Controllable action for engine tests: fails the first FailuresBeforeSuccess calls,
// then succeeds. Records every (template-resolved) config it receives.
public sealed class TestActionExecutor : IActionExecutor
{
    private int _calls;

    public string ActionType => "test.probe";

    public int Calls => _calls;

    public int FailuresBeforeSuccess { get; set; }

    public ConcurrentQueue<string> ReceivedConfigs { get; } = new();

    public void Reset(int failuresBeforeSuccess = 0)
    {
        _calls = 0;
        FailuresBeforeSuccess = failuresBeforeSuccess;
        ReceivedConfigs.Clear();
    }

    public Task<string?> ExecuteAsync(string configJson, ActionInvocation invocation, CancellationToken cancellationToken = default)
    {
        ReceivedConfigs.Enqueue(configJson);
        var call = Interlocked.Increment(ref _calls);
        // Echoes the resolved config into the output so masking is observable in tests.
        return call <= FailuresBeforeSuccess
            ? throw new InvalidOperationException($"probe failure {call}")
            : Task.FromResult<string?>($"ok:{call}:{configJson}");
    }
}

// Records every engine event; optionally throws on ExecutionStarted (after recording)
// to encode the rule that listener failures never affect engine flow.
public sealed class RecordingEventListener :
    IListenFor<ExecutionStarted>,
    IListenFor<StepCompleted>,
    IListenFor<StepFailed>,
    IListenFor<ExecutionCompleted>,
    IListenFor<ExecutionFailed>
{
    private readonly ConcurrentQueue<IEngineEvent> _events = new();

    public bool ThrowOnExecutionStarted { get; private set; }

    public IReadOnlyCollection<IEngineEvent> Events => _events;

    public void Reset(bool throwOnExecutionStarted = false)
    {
        _events.Clear();
        ThrowOnExecutionStarted = throwOnExecutionStarted;
    }

    public Task HandleAsync(ExecutionStarted engineEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(engineEvent);
        return ThrowOnExecutionStarted
            ? throw new InvalidOperationException("listener boom")
            : Task.CompletedTask;
    }

    public Task HandleAsync(StepCompleted engineEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(engineEvent);
        return Task.CompletedTask;
    }

    public Task HandleAsync(StepFailed engineEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(engineEvent);
        return Task.CompletedTask;
    }

    public Task HandleAsync(ExecutionCompleted engineEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(engineEvent);
        return Task.CompletedTask;
    }

    public Task HandleAsync(ExecutionFailed engineEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(engineEvent);
        return Task.CompletedTask;
    }
}

public sealed class EngineFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public IHost Host { get; private set; } = null!;

    public TestActionExecutor ProbeAction { get; } = new();

    public RecordingEventListener EventListener { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        // Same composition as the app: Program.cs and tests share AddAutomateXEngine,
        // so config drift between them is impossible by construction.
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Encryption:Key"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        });
        builder.AddAutomateXEngine(connectionString, options =>
        {
            // Tight retry ladder so failure tests run in milliseconds, not minutes.
            options.MaxStepAttempts = 3;
            options.StepRetryDelays = [TimeSpan.FromMilliseconds(50)];
            options.TriggerSyncInterval = TimeSpan.FromMilliseconds(500);
        });
        builder.Services.AddSingleton<IActionExecutor>(ProbeAction);
        builder.Services.AddSingleton<IEngineEventListener>(EventListener);
        // Trigger listeners defined in this assembly (e.g. test.tick) become live trigger types.
        builder.Services.AddSingleton<AutomateX.Engine.Triggers.ITriggerSource>(sp =>
            new AssemblyTriggerSource(typeof(EngineFixture).Assembly, sp));

        Host = builder.Build();

        await using (var scope = Host.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            await WorkspaceBootstrap.EnsureDefaultAsync(dbContext);
        }

        await Host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await Host.StopAsync();
        Host.Dispose();
        await _postgres.DisposeAsync();
    }
}

internal sealed class AssemblyTriggerSource(System.Reflection.Assembly assembly, IServiceProvider services)
    : AutomateX.Engine.Triggers.ITriggerSource
{
    public IEnumerable<AutomateX.Engine.Triggers.RegisteredTrigger> GetTriggers() =>
        AutomateX.Engine.Triggers.TriggerDiscovery.FromAssembly(assembly, "test", services);
}
