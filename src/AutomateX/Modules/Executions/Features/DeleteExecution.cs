using AutomateX.Database;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Executions.Features;

public static class DeleteExecution
{
    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access) : EndpointWithoutRequest
    {
        public override void Configure()
        {
            Delete("executions/{id}");
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

            if (!ExecutionDeleteRules.CanDelete(execution.Status))
            {
                ThrowError("Only finished executions can be deleted.");
            }

            // Step executions cascade at the DB level.
            await dbContext.Executions.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
            await Send.NoContentAsync(ct);
        }
    }
}
