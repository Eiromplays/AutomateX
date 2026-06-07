using AutomateX.Database;
using AutomateX.Modules.Workspaces;
using FastEndpoints;

namespace AutomateX.Modules.Workflows.Features;

public static class DeleteWorkflow
{
    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access) : EndpointWithoutRequest
    {
        public override void Configure()
        {
            Delete("workflows/{id}");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Editor, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            if (!await WorkflowDeletion.DeleteAsync(dbContext, Route<Guid>("id"), ws, ct))
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            await Send.NoContentAsync(ct);
        }
    }
}
