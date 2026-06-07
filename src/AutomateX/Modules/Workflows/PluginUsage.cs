using AutomateX.Database;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Workflows;

public static class PluginUsage
{
    // Latest versions only: history is pinned to the version it ran, and only the
    // latest version is what future executions (and restores) will reach for.
    public static async Task<List<string>> FindBlockingWorkflowsAsync(
        AutomateXDbContext dbContext,
        IReadOnlyCollection<string> actionTypes,
        Guid? workspaceId,
        CancellationToken cancellationToken)
    {
        if (actionTypes.Count == 0)
        {
            return [];
        }

        var workflows = await dbContext.Workflows
            .AsNoTracking()
            .Where(x => workspaceId == null || x.WorkspaceId == workspaceId)
            .Select(x => new
            {
                x.Name,
                Types = x.Versions
                    .OrderByDescending(v => v.Version)
                    .Take(1)
                    .SelectMany(v => v.Steps)
                    .Select(s => s.ActionType)
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        return workflows
            .Where(x => x.Types.Any(actionTypes.Contains))
            .Select(x => x.Name)
            .Order()
            .ToList();
    }
}
