using AutomateX.Database;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Executions.Features;

public static class GetExecutions
{
    public sealed class Endpoint(AutomateXDbContext dbContext) : EndpointWithoutRequest<List<Response>>
    {
        public override void Configure()
        {
            Get("executions");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            var executions = await dbContext.Executions
                .AsNoTracking()
                .OrderByDescending(x => x.StartedAt)
                .Take(50)
                .Select(x => new Response(
                    x.Id,
                    x.WorkflowId,
                    x.WorkflowVersionId,
                    x.TriggeredBy,
                    x.Status.ToString(),
                    x.StartedAt,
                    x.CompletedAt))
                .ToListAsync(ct);

            await Send.OkAsync(executions, ct);
        }
    }

    public sealed record Response(
        Guid Id,
        Guid WorkflowId,
        Guid WorkflowVersionId,
        string TriggeredBy,
        string Status,
        DateTimeOffset StartedAt,
        DateTimeOffset? CompletedAt);
}
