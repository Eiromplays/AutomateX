using AutomateX.Database;
using AutomateX.Modules.Audit;
using AutomateX.Modules.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// Audit trail: every execution settle writes an audit row (automatic, via the event bus), and the
// explicit IAuditSink records API-side mutations.
public sealed class AuditEngineTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private async Task<AuditEntry?> WaitForEntryAsync(string targetId, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = fixture.Host.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            var entry = await dbContext.AuditEntries.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TargetId == targetId);
            if (entry is not null)
            {
                return entry;
            }

            await Task.Delay(100);
        }

        return null;
    }

    [Fact]
    public async Task Succeeded_run_writes_an_audit_entry()
    {
        fixture.ProbeAction.Reset();
        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        var executionId = await TestData.ExecuteAsync(fixture.Host, workflowId);
        await TestData.WaitForCompletedAsync(fixture.Host, executionId, Timeout);

        var entry = await WaitForEntryAsync(executionId.ToString(), Timeout);
        Assert.NotNull(entry);
        Assert.Equal("execution.succeeded", entry.Action);
        Assert.Equal("execution", entry.TargetType);
        Assert.Equal("test", entry.Actor); // the run's trigger
    }

    [Fact]
    public async Task Failed_run_writes_a_failed_audit_entry()
    {
        fixture.ProbeAction.Reset(failuresBeforeSuccess: 99);
        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        var executionId = await TestData.ExecuteAsync(fixture.Host, workflowId);
        var run = await TestData.WaitForCompletedAsync(fixture.Host, executionId, Timeout);
        Assert.Equal(ExecutionStatus.Failed, run.Status);

        var entry = await WaitForEntryAsync(executionId.ToString(), Timeout);
        Assert.NotNull(entry);
        Assert.Equal("execution.failed", entry.Action);
    }

    [Fact]
    public async Task Read_scopes_to_a_workspace_for_members_but_is_global_for_admins()
    {
        var wsA = Guid.CreateVersion7();
        var wsB = Guid.CreateVersion7();
        var marker = $"test.{Guid.CreateVersion7():N}"; // unique action isolates these rows

        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
            db.AuditEntries.Add(AuditEntry.Create("a", wsA, marker, "x", "1", null));
            db.AuditEntries.Add(AuditEntry.Create("b", wsB, marker, "x", "2", null));
            await db.SaveChangesAsync();
        }

        await using var read = fixture.Host.Services.CreateAsyncScope();
        var dbContext = read.ServiceProvider.GetRequiredService<AutomateXDbContext>();

        var scoped = await AuditQuery.Apply(dbContext.AuditEntries.AsNoTracking(), wsA, action: marker).ToListAsync();
        Assert.Equal(wsA, Assert.Single(scoped).WorkspaceId);

        var global = await AuditQuery.Apply(dbContext.AuditEntries.AsNoTracking(), null, action: marker).ToListAsync();
        Assert.Equal(2, global.Count);
    }

    [Fact]
    public async Task Sink_records_an_explicit_mutation_entry()
    {
        var workspaceId = Guid.CreateVersion7();
        var targetId = Guid.CreateVersion7().ToString();

        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var sink = scope.ServiceProvider.GetRequiredService<IAuditSink>();
            await sink.RecordAsync("workflow.create", workspaceId, "alice@example.com", "workflow", targetId, "My flow");
        }

        var entry = await WaitForEntryAsync(targetId, Timeout);
        Assert.NotNull(entry);
        Assert.Equal("workflow.create", entry.Action);
        Assert.Equal(workspaceId, entry.WorkspaceId);
        Assert.Equal("alice@example.com", entry.Actor);
        Assert.Equal("My flow", entry.Summary);
    }
}
