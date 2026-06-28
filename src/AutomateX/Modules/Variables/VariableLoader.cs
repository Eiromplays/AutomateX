using AutomateX.Database;
using AutomateX.Engine.Security;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Variables;

// Loads a run's variables and collapses them to a name -> value map for the chosen environment.
// Decrypts secret values with the workspace DEK, then defers precedence/fallback to the pure
// VariableResolution core. Returns the resolved values plus the secret-name set for masking.
public sealed class VariableLoader(AutomateXDbContext dbContext, TenantCipher cipher)
{
    // The workspace's active environment (or 'default'), for contexts without an execution — preview
    // and the single-step test run.
    public async Task<Guid?> ActiveEnvironmentAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var active = await dbContext.Workspaces
            .AsNoTracking()
            .Where(x => x.Id == workspaceId)
            .Select(x => x.ActiveEnvironmentId)
            .FirstOrDefaultAsync(cancellationToken);

        if (active is { } id
            && await dbContext.WorkspaceEnvironments.AnyAsync(x => x.Id == id && x.WorkspaceId == workspaceId, cancellationToken))
        {
            return id;
        }

        return await dbContext.WorkspaceEnvironments
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.Name == WorkspaceEnvironment.DefaultName)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<(IReadOnlyDictionary<string, string> Values, IReadOnlySet<string> SecretNames)> LoadAsync(
        Guid workspaceId, Guid workflowId, Guid? environmentId, CancellationToken cancellationToken)
    {
        var defaultEnvironmentId = await dbContext.WorkspaceEnvironments
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.Name == WorkspaceEnvironment.DefaultName)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var environment = environmentId ?? defaultEnvironmentId;
        if (environment is null)
        {
            return (new Dictionary<string, string>(), new HashSet<string>());
        }

        var variables = await dbContext.Variables
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && (x.WorkflowId == null || x.WorkflowId == workflowId))
            .Include(x => x.Values)
            .ToListAsync(cancellationToken);

        List<VariableValueSet> sets = [];
        foreach (var variable in variables)
        {
            Dictionary<Guid, string> valuesByEnvironment = [];
            foreach (var value in variable.Values)
            {
                valuesByEnvironment[value.EnvironmentId] = variable.Secret
                    ? await cipher.DecryptAsync(value.Value, workspaceId, cancellationToken)
                    : value.Value;
            }

            sets.Add(new VariableValueSet(variable.Name, variable.WorkflowId is not null, variable.Secret, valuesByEnvironment));
        }

        return VariableResolution.Resolve(sets, environment.Value, defaultEnvironmentId ?? environment.Value);
    }
}
