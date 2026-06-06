using AutomateX.Plugin.Sdk;
using Microsoft.AspNetCore.SignalR;

namespace AutomateX.Web;

// Bridges best-effort engine events onto SignalR for live UI updates —
// the same IListenFor<T> seam plugins use.
public sealed class SignalRExecutionEventListener(IHubContext<ExecutionEventsHub> hubContext) :
    IListenFor<ExecutionStarted>,
    IListenFor<StepCompleted>,
    IListenFor<StepFailed>,
    IListenFor<ExecutionCompleted>,
    IListenFor<ExecutionFailed>
{
    public Task HandleAsync(ExecutionStarted engineEvent, CancellationToken cancellationToken = default) =>
        BroadcastAsync(nameof(ExecutionStarted), engineEvent, cancellationToken);

    public Task HandleAsync(StepCompleted engineEvent, CancellationToken cancellationToken = default) =>
        BroadcastAsync(nameof(StepCompleted), engineEvent, cancellationToken);

    public Task HandleAsync(StepFailed engineEvent, CancellationToken cancellationToken = default) =>
        BroadcastAsync(nameof(StepFailed), engineEvent, cancellationToken);

    public Task HandleAsync(ExecutionCompleted engineEvent, CancellationToken cancellationToken = default) =>
        BroadcastAsync(nameof(ExecutionCompleted), engineEvent, cancellationToken);

    public Task HandleAsync(ExecutionFailed engineEvent, CancellationToken cancellationToken = default) =>
        BroadcastAsync(nameof(ExecutionFailed), engineEvent, cancellationToken);

    private Task BroadcastAsync(string type, object payload, CancellationToken cancellationToken) =>
        hubContext.Clients.All.SendAsync("engineEvent", new { type, payload }, cancellationToken);
}
