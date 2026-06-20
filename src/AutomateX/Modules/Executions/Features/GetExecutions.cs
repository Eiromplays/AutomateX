using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Executions.Features;

public static class GetExecutions
{
    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access) : EndpointWithoutRequest<List<Response>>
    {
        public override void Configure()
        {
            Get("executions");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Viewer, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            // Page size: newest-first, capped so a long-lived instance never streams its whole
            // history. The UI bumps `take` for "Load more".
            var take = Math.Clamp(Query<int?>("take", isRequired: false) ?? 50, 1, 500);

            var rows = await dbContext.Executions
                .AsNoTracking()
                .Where(x => x.WorkspaceId == ws)
                .OrderByDescending(x => x.StartedAt)
                .Take(take)
                .Select(x => new
                {
                    x.Id,
                    x.WorkflowId,
                    WorkflowName = dbContext.Workflows.Where(w => w.Id == x.WorkflowId).Select(w => w.Name).FirstOrDefault() ?? "(deleted)",
                    x.WorkflowVersionId,
                    x.TriggeredBy,
                    Status = x.Status.ToString(),
                    x.StartedAt,
                    x.CompletedAt,
                    ChainPayload = x.TriggeredBy == WorkflowChaining.TriggerType ? x.TriggerPayload : null,
                })
                .ToListAsync(ct);

            var executions = rows
                .Select(x => new Response(
                    x.Id,
                    x.WorkflowId,
                    x.WorkflowName,
                    x.WorkflowVersionId,
                    x.TriggeredBy,
                    x.Status,
                    x.StartedAt,
                    x.CompletedAt,
                    WorkflowChaining.GetSourceExecutionId(x.ChainPayload)
                        ?? ExecutionRetry.GetOriginalExecutionId(x.TriggeredBy)))
                .ToList();

            await Send.OkAsync(executions, ct);
        }
    }

    public sealed record Response(
        Guid Id,
        Guid WorkflowId,
        string WorkflowName,
        Guid WorkflowVersionId,
        string TriggeredBy,
        string Status,
        DateTimeOffset StartedAt,
        DateTimeOffset? CompletedAt,
        Guid? ParentExecutionId);
}
