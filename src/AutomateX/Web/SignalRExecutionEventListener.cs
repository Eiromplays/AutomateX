using AutomateX.Database;
using AutomateX.Plugin.Sdk;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Web;

// Bridges best-effort engine events onto SignalR for live UI updates. Events route
// to the execution's workspace group only — so the audience is authorized, and the
// full (already-masked) payload ships, letting clients patch caches without refetching.
public sealed class SignalRExecutionEventListener(
    IHubContext<ExecutionEventsHub> hubContext,
    IServiceScopeFactory scopeFactory) :
    IListenFor<ExecutionStarted>,
    IListenFor<StepCompleted>,
    IListenFor<StepFailed>,
    IListenFor<ExecutionCompleted>,
    IListenFor<ExecutionFailed>
{
    public Task HandleAsync(ExecutionStarted e, CancellationToken ct = default) =>
        BroadcastAsync(e.ExecutionId, nameof(ExecutionStarted), e, ct);

    public Task HandleAsync(StepCompleted e, CancellationToken ct = default) =>
        BroadcastAsync(e.ExecutionId, nameof(StepCompleted), e, ct);

    public Task HandleAsync(StepFailed e, CancellationToken ct = default) =>
        BroadcastAsync(e.ExecutionId, nameof(StepFailed), e, ct);

    public Task HandleAsync(ExecutionCompleted e, CancellationToken ct = default) =>
        BroadcastAsync(e.ExecutionId, nameof(ExecutionCompleted), e, ct);

    public Task HandleAsync(ExecutionFailed e, CancellationToken ct = default) =>
        BroadcastAsync(e.ExecutionId, nameof(ExecutionFailed), e, ct);

    // Generic over the concrete event type: a payload declared as `object` would
    // serialize as `{}` (System.Text.Json uses the declared type), so the wrapper's
    // `payload` property must keep the concrete record type for its fields to ship.
    private async Task BroadcastAsync<TEvent>(Guid executionId, string type, TEvent payload, CancellationToken ct)
        where TEvent : IEngineEvent
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();

        var workspaceId = await dbContext.Executions
            .AsNoTracking()
            .Where(x => x.Id == executionId)
            .Select(x => (Guid?)x.WorkspaceId)
            .FirstOrDefaultAsync(ct);

        if (workspaceId is null)
        {
            return; // execution vanished (e.g. retention) — nothing to broadcast
        }

        await hubContext.Clients
            .Group(ExecutionEventGroups.ForWorkspace(workspaceId.Value))
            .SendAsync("engineEvent", new { type, payload }, ct);
    }
}
