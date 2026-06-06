using AutomateX.Plugin.Sdk;

namespace AutomateX.PluginTemplate;

public sealed record GreetConfig(string Name);

public sealed record GreetResult(string Greeting);

[Action("greet.hello", "Greet", Description = "Says hello.")]
public sealed class GreetAction : IAction<GreetConfig, GreetResult>
{
    public Task<GreetResult> ExecuteAsync(
        GreetConfig config,
        ActionContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new GreetResult($"Hello {config.Name}!"));
}
