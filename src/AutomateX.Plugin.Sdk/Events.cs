namespace AutomateX.Plugin.Sdk;

public interface IEngineEvent;

// Marker for DI/discovery; implement IListenFor<TEvent> for each event of interest.
public interface IEngineEventListener;

// Engine events are best-effort, in-process notifications: published after state is
// persisted, never part of the engine's transactional flow. A listener that throws is
// logged and skipped — it can never affect an execution's outcome.
public interface IListenFor<in TEvent> : IEngineEventListener
    where TEvent : IEngineEvent
{
    Task HandleAsync(TEvent engineEvent, CancellationToken cancellationToken = default);
}

public sealed record ExecutionStarted(Guid ExecutionId, Guid WorkflowId, string TriggeredBy) : IEngineEvent;

public sealed record StepCompleted(Guid ExecutionId, int StepOrder, string ActionType, string? Output) : IEngineEvent;

public sealed record StepFailed(
    Guid ExecutionId,
    int StepOrder,
    string ActionType,
    string Error,
    int Attempts,
    bool WillRetry) : IEngineEvent;

public sealed record ExecutionCompleted(Guid ExecutionId, Guid WorkflowId) : IEngineEvent;

public sealed record ExecutionFailed(Guid ExecutionId, Guid WorkflowId) : IEngineEvent;
