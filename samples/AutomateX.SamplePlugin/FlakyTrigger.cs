using AutomateX.Plugin.Sdk;

namespace AutomateX.SamplePlugin;

public sealed record FlakyConfig(int FailTimes = 1);

// Throws on its first FailTimes runs, then fires once and parks — exercises the host's self-healing
// listener loop (a throw must restart the listener, not kill it). Attempt count survives restarts via
// durable trigger state.
[Trigger("sample.flaky", "Flaky", Description = "Fails a few times then fires — for listener-restart tests.")]
public sealed class FlakyTrigger : ITriggerListener<FlakyConfig>
{
    public async Task RunAsync(FlakyConfig config, TriggerContext context, CancellationToken cancellationToken)
    {
        var attempts = int.TryParse(await context.State.GetAsync("attempts", cancellationToken), out var n) ? n : 0;
        await context.State.SetAsync("attempts", (attempts + 1).ToString(), cancellationToken: cancellationToken);

        if (attempts < config.FailTimes)
        {
            throw new InvalidOperationException($"flaky attempt {attempts + 1}");
        }

        await context.FireAsync($$"""{"firedAfter":{{attempts}}}""");
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}
