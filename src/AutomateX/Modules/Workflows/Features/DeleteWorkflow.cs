using AutomateX.Database;
using AutomateX.Modules.Workspaces;
using FastEndpoints;

namespace AutomateX.Modules.Workflows.Features;

public static class DeleteWorkflow
{
    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access, Audit.IAuditSink audit) : EndpointWithoutRequest
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

            var id = Route<Guid>("id");
            if (!await WorkflowDeletion.DeleteAsync(dbContext, id, ws, ct))
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            await audit.RecordAsync("workflow.delete", ws, WorkspaceAccess.GetActor(User), "workflow", id.ToString(), null, ct);
            await Send.NoContentAsync(ct);
        }
    }
}
