namespace AutomateX.Plugin.Sdk;

// The whole authoring contract: a config record in, a result record out.
// The config type doubles as the UI form schema; the result is persisted as step output.
public interface IAction<in TConfig, TResult>
{
    Task<TResult> ExecuteAsync(TConfig config, ActionContext context, CancellationToken cancellationToken = default);
}
