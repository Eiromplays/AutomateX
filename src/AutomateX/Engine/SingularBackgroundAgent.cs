using Wolverine.Runtime.Agents;

namespace AutomateX.Engine;

// A Wolverine SingularAgent that hosts one long-lived loop, assigned by leader election to exactly one node
// in the cluster (and re-homed on node loss). Subclasses implement the loop in RunAsync; this base owns its
// lifetime. In Solo mode every agent runs on the single node, so single-node behaviour is unchanged.
public abstract class SingularBackgroundAgent(string scheme) : SingularAgent(scheme)
{
    private CancellationTokenSource? _cts;
    private Task? _loop;

    protected abstract Task RunAsync(CancellationToken stoppingToken);

    protected sealed override Task startAsync(CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunGuardedAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    protected sealed override async Task stopAsync(CancellationToken cancellationToken)
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
        }

        if (_loop is not null)
        {
            try
            {
                await _loop;
            }
            catch (OperationCanceledException)
            {
                // expected when the agent is reassigned or the host stops
            }
        }

        // The same agent instance is reused across reassignments — reset so the next startAsync is clean.
        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }

    private async Task RunGuardedAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // normal stop path
        }
    }
}
