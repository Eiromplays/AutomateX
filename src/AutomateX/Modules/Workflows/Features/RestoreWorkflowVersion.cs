using AutomateX.Database;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Workflows.Features;

public static class RestoreWorkflowVersion
{
    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access) : EndpointWithoutRequest<Response>
    {
        public override void Configure()
        {
            Post("workflows/{id}/versions/{version}/restore");
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
                .ThenInclude(x => x.Steps)
                .FirstOrDefaultAsync(x => x.Id == id && x.WorkspaceId == ws, ct);

            if (workflow is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            WorkflowVersion restored;
            try
            {
                restored = workflow.RestoreVersion(versionNumber);
            }
            catch (InvalidOperationException exception)
            {
                ThrowError(exception.Message);
                return;
            }

            // Explicit Add — discovered children with client-set keys track as Modified.
            dbContext.WorkflowVersions.Add(restored);
            await dbContext.SaveChangesAsync(ct);

            await Send.OkAsync(new Response(workflow.Id, restored.Id, restored.Version), ct);
        }
    }

    public sealed record Response(Guid Id, Guid VersionId, int Version);
}
