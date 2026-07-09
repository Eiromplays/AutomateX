using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AutomateX.Database;

public static class MigrationExtensions
{
    // App-specific advisory-lock key, distinct from Wolverine's migration lock (4006) and Marten's (4004)
    // so they never contend.
    private const long MigrationLockKey = 415500100;

    // Serialises EF MigrateAsync across booting nodes with a session-level Postgres advisory lock. EF Core's
    // own migration lock does NOT reliably serialise the first migration on a fresh database with the Npgsql
    // provider (verified: concurrent migrators collide with `42701 column ... already exists`), so we hold
    // this lock across MigrateAsync ourselves: exactly one node migrates at a time; the rest wait, then find
    // nothing pending. The lock lives on a dedicated connection so its session lifetime is explicit and
    // independent of EF's own migration connection/transactions.
    public static async Task MigrateWithAdvisoryLockAsync(
        this AutomateXDbContext dbContext, string connectionString, CancellationToken cancellationToken = default)
    {
        // Dedicated connection for the lock (passed in, not read off the context — Aspire may configure the
        // context via an NpgsqlDataSource, leaving GetConnectionString null).
        await using var lockConnection = new NpgsqlConnection(connectionString);
        await lockConnection.OpenAsync(cancellationToken);

        await ExecuteAsync(lockConnection, $"SELECT pg_advisory_lock({MigrationLockKey}::bigint)", cancellationToken);
        try
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
        finally
        {
            await ExecuteAsync(lockConnection, $"SELECT pg_advisory_unlock({MigrationLockKey}::bigint)", cancellationToken);
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
