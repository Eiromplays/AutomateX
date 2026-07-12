using AutomateX.Plugin.Sdk;
using Humanizer;

namespace AutomateX.SamplePlugin;

public sealed record HumanizeConfig(string Text);

public sealed record HumanizeResult(string Humanized);

// Uses the Humanizer package — a plugin-private dependency the host does not reference — so the out-of-proc
// dependency test can prove the plugin host's AssemblyLoadContext resolves it from the plugin's deps.json.
[Action("sample.humanize", "Humanize", Description = "Humanizes a string via the Humanizer package.")]
public sealed class HumanizeAction : IAction<HumanizeConfig, HumanizeResult>
{
    public Task<HumanizeResult> ExecuteAsync(
        HumanizeConfig config,
        ActionContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new HumanizeResult(config.Text.Humanize()));
}
