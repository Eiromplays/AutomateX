using AutomateX.Database;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Triggers.Features;

public static class DeleteTrigger
{
    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access, Audit.IAuditSink audit) : EndpointWithoutRequest
    {
        public override void Configure()
        {
            Delete("triggers/{id}");
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

            var deleted = await dbContext.Triggers
                .Where(x => x.Id == id
                    && dbContext.Workflows.Any(w => w.Id == x.WorkflowId && w.WorkspaceId == ws))
                .ExecuteDeleteAsync(ct);

            if (deleted == 0)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            await audit.RecordAsync("trigger.delete", ws, WorkspaceAccess.GetActor(User), "trigger", id.ToString(), null, ct);
            await Send.NoContentAsync(ct);
        }
    }
}
