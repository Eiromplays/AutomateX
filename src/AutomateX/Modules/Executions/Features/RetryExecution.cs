using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace AutomateX.Modules.Executions.Features;

public static class RetryExecution
{
    public sealed class Endpoint(
        AutomateXDbContext dbContext,
        IMessageBus bus,
        WorkspaceAccess access) : EndpointWithoutRequest<Response>
    {
        public override void Configure()
        {
            Post("executions/{id}/retry");
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
            var execution = await dbContext.Executions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id && x.WorkspaceId == ws, ct);

            if (execution is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            if (!ExecutionRetry.CanRetry(execution.Status))
            {
                ThrowError("Only finished executions can be retried.");
            }

            if (!await dbContext.Workflows.AnyAsync(x => x.Id == execution.WorkflowId && x.WorkspaceId == ws, ct))
            {
                ThrowError("The workflow this execution ran no longer exists.");
            }

            var message = ExecutionRetry.Replay(execution);
            await bus.PublishAsync(message);

            await Send.OkAsync(new Response(message.ExecutionId), ct);
        }
    }

    public sealed record Response(Guid ExecutionId);
}
