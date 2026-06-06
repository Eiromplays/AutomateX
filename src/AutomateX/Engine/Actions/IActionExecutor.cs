using System.Text.Json;

namespace AutomateX.Engine.Actions;

public interface IActionExecutor
{
    string ActionType { get; }

    Task<string?> ExecuteAsync(string configJson, CancellationToken cancellationToken = default);
}

public abstract class ActionExecutor<TConfig, TResult>(string actionType) : IActionExecutor
{
    public string ActionType { get; } = actionType;

    public async Task<string?> ExecuteAsync(string configJson, CancellationToken cancellationToken = default)
    {
        var config = JsonSerializer.Deserialize<TConfig>(configJson, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException($"Invalid config for action '{ActionType}'.");

        var result = await ExecuteAsync(config, cancellationToken);

        return result is null ? null : JsonSerializer.Serialize(result, JsonSerializerOptions.Web);
    }

    protected abstract Task<TResult> ExecuteAsync(TConfig config, CancellationToken cancellationToken);
}
