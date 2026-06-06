using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Engine.Actions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Xunit;

namespace AutomateX.Tests;

// Controllable action for engine tests: fails the first FailuresBeforeSuccess calls, then succeeds.
public sealed class TestActionExecutor : IActionExecutor
{
    private int _calls;

    public string ActionType => "test.probe";

    public int Calls => _calls;

    public int FailuresBeforeSuccess { get; set; }

    public void Reset(int failuresBeforeSuccess = 0)
    {
        _calls = 0;
        FailuresBeforeSuccess = failuresBeforeSuccess;
    }

    public Task<string?> ExecuteAsync(string configJson, CancellationToken cancellationToken = default)
    {
        var call = Interlocked.Increment(ref _calls);
        return call <= FailuresBeforeSuccess
            ? throw new InvalidOperationException($"probe failure {call}")
            : Task.FromResult<string?>($"ok:{call}");
    }
}

public sealed class EngineFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public IHost Host { get; private set; } = null!;

    public TestActionExecutor ProbeAction { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .UseWolverine(opts =>
            {
                // In a test host the "application assembly" is the test assembly —
                // point handler discovery at the engine assembly explicitly.
                opts.ApplicationAssembly = typeof(RunWorkflow).Assembly;

                // Single ephemeral node: skip leadership election so agents start immediately.
                opts.Durability.Mode = DurabilityMode.Solo;

                opts.PersistMessagesWithPostgresql(connectionString, "wolverine");
                opts.Policies.UseDurableLocalQueues();
                opts.UseEntityFrameworkCoreTransactions();
                opts.Policies.AutoApplyTransactions();
            })
            .ConfigureServices(services =>
            {
                services.AddDbContextWithWolverineIntegration<AutomateXDbContext>(
                    options => options.UseNpgsql(connectionString),
                    "wolverine");

                services.AddSingleton<IActionExecutor>(ProbeAction);
                services.AddSingleton<ActionRegistry>();

                // Tight retry ladder so failure tests run in milliseconds, not minutes.
                services.Configure<EngineOptions>(options =>
                {
                    options.MaxStepAttempts = 3;
                    options.StepRetryDelays = [TimeSpan.FromMilliseconds(50)];
                });
            })
            .Build();

        await using (var scope = Host.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
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
