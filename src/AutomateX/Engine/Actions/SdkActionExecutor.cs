using System.Text.Json;
using AutomateX.Plugin.Sdk;

namespace AutomateX.Engine.Actions;

public sealed class SdkActionExecutor<TConfig, TResult>(
    IAction<TConfig, TResult> action,
    string actionType,
    ActionContextFactory contextFactory) : IActionExecutor
{
    public string ActionType => actionType;

    public async Task<string?> ExecuteAsync(string configJson, CancellationToken cancellationToken = default)
    {
        var config = JsonSerializer.Deserialize<TConfig>(configJson, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException($"Invalid config for action '{actionType}'.");

        var result = await action.ExecuteAsync(config, contextFactory.Create(actionType), cancellationToken);

        return result is null ? null : JsonSerializer.Serialize(result, JsonSerializerOptions.Web);
    }
}
