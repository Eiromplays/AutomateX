using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace AutomateX.Modules.Executions.Features;

// Re-run a finished execution from a chosen step, reusing the prior run's upstream outputs.
public static class RetryExecutionFromStep
{
    public sealed class Endpoint(
        AutomateXDbContext dbContext,
        IMessageBus bus,
        WorkspaceAccess access) : EndpointWithoutRequest<Response>
    {
        public override void Configure()
        {
            Post("executions/{id}/retry-from/{order}");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Editor, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var id = Route<Guid>("id");
            var order = Route<int>("order");

            var source = await dbContext.Executions
                .AsNoTracking()
                .Where(x => x.Id == id && x.WorkspaceId == ws)
                .Select(x => new { x.Status, x.WorkflowVersionId })
                .FirstOrDefaultAsync(ct);

            if (source is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            if (!ExecutionRetry.CanRetry(source.Status))
            {
                ThrowError("Only finished executions can be retried.");
            }

            var stepCount = await dbContext.WorkflowSteps
                .CountAsync(x => x.WorkflowVersionId == source.WorkflowVersionId, ct);
            if (order < 0 || order >= stepCount)
            {
                ThrowError("That step is out of range for this execution.");
            }

            var newId = Guid.CreateVersion7();
            await bus.PublishAsync(new RetryFromStep(newId, id, order));
            await Send.OkAsync(new Response(newId), ct);
        }
    }

    public sealed record Response(Guid ExecutionId);
}
