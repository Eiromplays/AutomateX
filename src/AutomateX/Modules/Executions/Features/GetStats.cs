using AutomateX.Database;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Executions.Features;

public static class GetStats
{
    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access) : EndpointWithoutRequest<ExecutionStats>
    {
        public override void Configure()
        {
            Get("stats");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Viewer, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var days = 14;
            if (int.TryParse(HttpContext.Request.Query["days"], out var requested) && requested is > 0 and <= 90)
            {
                days = requested;
            }

            var now = DateTimeOffset.UtcNow;
            var cutoff = now.AddDays(-days);

            var rows = await dbContext.Executions
                .AsNoTracking()
                .Where(x => x.WorkspaceId == ws && x.StartedAt >= cutoff)
                .Select(x => new ExecutionRow(x.Id, x.WorkflowId, x.Status, x.StartedAt, x.CompletedAt))
                .ToListAsync(ct);

            var workflowIds = rows.Select(x => x.WorkflowId).Distinct().ToList();
            var names = await dbContext.Workflows
                .AsNoTracking()
                .Where(x => workflowIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

            await Send.OkAsync(StatsCalculator.Compute(rows, names, now, days), ct);
        }
    }
}
