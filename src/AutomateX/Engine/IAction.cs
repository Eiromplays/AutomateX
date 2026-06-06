namespace AutomateX.Engine;

// Seed of the plugin SDK contract — M2 expands this with ActionContext, metadata
// and AssemblyLoadContext-based plugin loading (docs/v2-plan.md §7).
public interface IAction<in TConfig, TResult>
{
    Task<TResult> ExecuteAsync(TConfig config, CancellationToken cancellationToken = default);
}
