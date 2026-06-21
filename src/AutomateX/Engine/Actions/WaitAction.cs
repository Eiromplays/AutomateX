using AutomateX.Plugin.Sdk;

namespace AutomateX.Engine.Actions;

public sealed record WaitConfig(
    string? Mode = null,
    int? DelaySeconds = null,
    DateTimeOffset? Until = null,
    int? TimeoutSeconds = null);

// A trivial result type for schema/discovery only — the engine intercepts `wait` in
// ExecuteStepHandler and never invokes the action (the step output is the resume payload).
public sealed record WaitResult(string Reason);

// Pure wait semantics, shared by the engine.
public static class Wait
{
    public const string ActionType = "wait";
    public const string SignalMode = "signal";
    public const string DelayMode = "delay";

    // Signal waits resume on an external call; delay waits resume on a timer. Mode is explicit,
    // or inferred: a delaySeconds/until makes it a delay wait, otherwise it waits for a signal.
    public static bool IsSignal(WaitConfig config) =>
        config.Mode == SignalMode || (config.Mode != DelayMode && config.DelaySeconds is null && config.Until is null);

    // When the timer should wake the run, or null for an indefinite signal wait. Delay waits always
    // have a wake; signal waits only when a timeoutSeconds is set.
    public static DateTimeOffset? WakeAt(WaitConfig config, DateTimeOffset now)
    {
        if (IsSignal(config))
        {
            return config.TimeoutSeconds is { } timeout
                ? now.AddSeconds(RequirePositive(timeout, "timeoutSeconds"))
                : null;
        }

        if (config.Until is { } until)
        {
            if (config.DelaySeconds is not null)
            {
                throw new ArgumentException("wait takes 'delaySeconds' or 'until', not both.");
            }

            return until;
        }

        if (config.DelaySeconds is { } delay)
        {
            return now.AddSeconds(RequirePositive(delay, "delaySeconds"));
        }

        throw new ArgumentException("a delay wait needs 'delaySeconds' or 'until'.");
    }

    private static int RequirePositive(int value, string field) =>
        value >= 0 ? value : throw new ArgumentException($"wait '{field}' must be zero or positive.");
}

[Action("wait", "Wait / Approval",
    Description = "Pause the run until resumed. Set delaySeconds or until for a timed wait, or mode "
        + "'signal' (with optional timeoutSeconds) to wait for an approval/resume. The resume payload "
        + "becomes this step's output — branch on it with a following gate or switch.")]
public sealed class WaitAction : IAction<WaitConfig, WaitResult>
{
    public Task<WaitResult> ExecuteAsync(WaitConfig config, ActionContext context, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("wait is handled by the engine and is never executed directly.");
}
