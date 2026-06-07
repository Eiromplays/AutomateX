using AutomateX.Plugin.Sdk;

namespace AutomateX.SamplePlugin;

public sealed record TickerConfig(int IntervalMilliseconds = 5000, int? MaxFires = null);

[Trigger("sample.ticker", "Ticker", Description = "Fires on a fixed interval — the trigger-plugin hello world.")]
public sealed class TickerTrigger : ITriggerListener<TickerConfig>
{
    public async Task RunAsync(TickerConfig config, TriggerContext context, CancellationToken cancellationToken)
    {
        var count = 0;
        while (!cancellationToken.IsCancellationRequested && (config.MaxFires is null || count < config.MaxFires))
        {
            await Task.Delay(config.IntervalMilliseconds, cancellationToken);
            count++;
            await context.FireAsync($$"""{"tick":{{count}},"firedAt":"{{DateTimeOffset.UtcNow:O}}"}""");
        }

        // MaxFires reached: park until cancelled so the host doesn't re-run the cycle.
        if (config.MaxFires is not null)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
