using AutomateX.Plugin.Sdk;

namespace AutomateX.SamplePlugin;

public sealed record DelayConfig(int Milliseconds);

public sealed record DelayResult(int Milliseconds);

[Action("sample.delay", "Delay", Description = "Waits for the configured duration — handy for crash/resume testing.")]
public sealed class DelayAction : IAction<DelayConfig, DelayResult>
{
    public async Task<DelayResult> ExecuteAsync(
        DelayConfig config,
        ActionContext context,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(config.Milliseconds, cancellationToken);
        return new DelayResult(config.Milliseconds);
    }
}
