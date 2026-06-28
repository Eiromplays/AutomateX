using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Modules.Audit;
using AutomateX.Modules.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// Retention pruning deletes rows past the window and leaves fresh ones — isolated with unique markers
// and backdated via ExecuteUpdate (the timestamp is set at creation, so we can't pass an old one).
public sealed class RetentionSweeperTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    [Fact]
    public async Task Prunes_audit_entries_past_the_window_only()
    {
        var stale = $"stale.{Guid.CreateVersion7():N}";
        var fresh = $"fresh.{Guid.CreateVersion7():N}";

        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            db.AuditEntries.Add(AuditEntry.Create(stale, null, "test", null, null, null));
            db.AuditEntries.Add(AuditEntry.Create(fresh, null, "test", null, null, null));
            await db.SaveChangesAsync();
            await db.AuditEntries.Where(x => x.Actor == stale)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.At, DateTimeOffset.UtcNow.AddDays(-100)));
        }

        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            await RetentionSweeper.PruneAuditAsync(db, DateTimeOffset.UtcNow.AddDays(-30));

            Assert.Equal(0, await db.AuditEntries.CountAsync(x => x.Actor == stale));
            Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.Actor == fresh));
        }
    }

    [Fact]
    public async Task Prunes_idempotency_records_past_the_window_only()
    {
        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        var stale = $"stale.{Guid.CreateVersion7():N}";
        var fresh = $"fresh.{Guid.CreateVersion7():N}";

        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            var ws = await db.Workflows.Where(x => x.Id == workflowId).Select(x => x.WorkspaceId).FirstAsync();
            db.IdempotencyRecords.Add(IdempotencyRecord.Create(ws, workflowId, stale, "r"));
            db.IdempotencyRecords.Add(IdempotencyRecord.Create(ws, workflowId, fresh, "r"));
            await db.SaveChangesAsync();
            await db.IdempotencyRecords.Where(x => x.Key == stale)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, DateTimeOffset.UtcNow.AddDays(-100)));
        }

        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            await RetentionSweeper.PruneIdempotencyAsync(db, DateTimeOffset.UtcNow.AddDays(-30));

            Assert.Equal(0, await db.IdempotencyRecords.CountAsync(x => x.Key == stale));
            Assert.Equal(1, await db.IdempotencyRecords.CountAsync(x => x.Key == fresh));
        }
    }
}
