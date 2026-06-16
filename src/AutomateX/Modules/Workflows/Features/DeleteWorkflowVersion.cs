using AutomateX.Database;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Workflows.Features;

// Deletes a single past version to cut history clutter (e.g. lots of test iterations). Guarded:
// the latest version stays (it's the live definition), and a version any execution ran on stays
// so the run-graph can still show what executed.
public static class DeleteWorkflowVersion
{
    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access) : EndpointWithoutRequest
    {
        public override void Configure()
        {
            Delete("workflows/{id}/versions/{version}");
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
            var versionNumber = Route<int>("version");

            var workflow = await dbContext.Workflows
                .Include(x => x.Versions)
                .FirstOrDefaultAsync(x => x.Id == id && x.WorkspaceId == ws, ct);

            var target = workflow?.Versions.FirstOrDefault(x => x.Version == versionNumber);
            if (workflow is null || target is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            if (await dbContext.Executions.AnyAsync(x => x.WorkflowVersionId == target.Id, ct))
            {
                ThrowError("This version has executions that ran on it — it's kept so their run graph stays intact.");
                return;
            }

            try
            {
                workflow.RemoveVersion(versionNumber);
            }
            catch (InvalidOperationException exception)
            {
                ThrowError(exception.Message);
                return;
            }

            dbContext.WorkflowVersions.Remove(target);
            await dbContext.SaveChangesAsync(ct);
            await Send.NoContentAsync(ct);
        }
    }
}
