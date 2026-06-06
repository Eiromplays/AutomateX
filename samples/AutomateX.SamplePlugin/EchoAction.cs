using AutomateX.Plugin.Sdk;
using Microsoft.Extensions.Logging;

namespace AutomateX.SamplePlugin;

public sealed record EchoConfig(string Message);

public sealed record EchoResult(string Message, DateTimeOffset EchoedAt);

[Action("sample.echo", "Echo", Description = "Logs and returns the configured message.")]
public sealed class EchoAction : IAction<EchoConfig, EchoResult>
{
    public Task<EchoResult> ExecuteAsync(
        EchoConfig config,
        ActionContext context,
        CancellationToken cancellationToken = default)
    {
        context.Logger.LogInformation("Echo: {Message}", config.Message);
        return Task.FromResult(new EchoResult(config.Message, DateTimeOffset.UtcNow));
    }
}
