using AutomateX.Database;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace AutomateX.Tests;

// v5.0 m6: every node runs Database:MigrateOnStartup, so N MigrateAsync calls can hit one fresh database at
// once. EF Core's own migration lock does NOT reliably serialise the first migration on a fresh database with
// the Npgsql provider — plain concurrent MigrateAsync collides here with `42701 column ... already exists` —
// so MigrateWithAdvisoryLockAsync holds a Postgres advisory lock across the migration. This test pins that:
// two concurrent migrators against one database must both complete without throwing, leaving a usable schema.
public sealed class StartupMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private AutomateXDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AutomateXDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new AutomateXDbContext(options);
    }

    [Fact]
    public async Task Concurrent_startup_migrators_serialize_and_do_not_throw()
    {
        await using var nodeA = NewContext();
        await using var nodeB = NewContext();

        // Both nodes boot at once against the same empty database.
        var connectionString = _postgres.GetConnectionString();
        await Task.WhenAll(
            nodeA.MigrateWithAdvisoryLockAsync(connectionString),
            nodeB.MigrateWithAdvisoryLockAsync(connectionString));

        // Schema applied and usable — a query against a migrated table succeeds.
        await using var verify = NewContext();
        Assert.Equal(0, await verify.Workflows.CountAsync());
    }
}
