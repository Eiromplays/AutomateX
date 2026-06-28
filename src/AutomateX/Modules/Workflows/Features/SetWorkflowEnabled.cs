using AutomateX.Database;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Workflows.Features;

// Pause/resume a workflow. Disabled workflows are dropped at RunWorkflowHandler, so this gates
// every path (triggers, chains, scheduled, manual) at once.
public static class SetWorkflowEnabled
{
    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access, Audit.IAuditSink audit) : Endpoint<Request>
    {
        public override void Configure()
        {
            Put("workflows/{id}/enabled");
            AllowAnonymous();
        }

        public override async Task HandleAsync(Request req, CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Editor, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var workflow = await dbContext.Workflows
                .FirstOrDefaultAsync(x => x.Id == Route<Guid>("id") && x.WorkspaceId == ws, ct);

            if (workflow is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            workflow.SetEnabled(req.Enabled);
            await dbContext.SaveChangesAsync(ct);
            await audit.RecordAsync(
                req.Enabled ? "workflow.enable" : "workflow.disable", ws, WorkspaceAccess.GetActor(User),
                "workflow", workflow.Id.ToString(), workflow.Name, ct);
            await Send.NoContentAsync(ct);
        }
    }

    public sealed record Request(bool Enabled);
}
