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
            var id = Route<Guid>("id");
            if (await access.AuthorizeWorkflowAsync(HttpContext, id, WorkspaceRole.Editor, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            if (!await WorkflowDeletion.DeleteAsync(dbContext, id, ws, ct))
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            await audit.RecordAsync("workflow.delete", ws, WorkspaceAccess.GetActor(HttpContext), "workflow", id.ToString(), null, ct);
            await Send.NoContentAsync(ct);
        }
    }
}
