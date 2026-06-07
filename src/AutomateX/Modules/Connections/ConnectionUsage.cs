using System.Text.RegularExpressions;
using AutomateX.Database;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Connections;

public static class ConnectionUsage
{
    // Latest versions only, same workspace only (connections are workspace-scoped).
    // The dot after the name anchors against prefix collisions (deploy vs deployment).
    public static async Task<List<string>> FindBlockingWorkflowsAsync(
        AutomateXDbContext dbContext,
        string connectionName,
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var reference = new Regex(
            @"\{\{\s*connections\." + Regex.Escape(connectionName) + @"\.",
            RegexOptions.None,
            TimeSpan.FromSeconds(1));

        var workflows = await dbContext.Workflows
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId)
            .Select(x => new
            {
                x.Name,
                Configs = x.Versions
                    .OrderByDescending(v => v.Version)
                    .Take(1)
                    .SelectMany(v => v.Steps)
                    .Select(s => s.ConfigJson)
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        return workflows
            .Where(x => x.Configs.Any(config => reference.IsMatch(config)))
            .Select(x => x.Name)
            .Order()
            .ToList();
    }
}
