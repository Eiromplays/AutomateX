using AutomateX.Database;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Connections.Features;

public static class DeleteConnection
{
    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access) : EndpointWithoutRequest
    {
        public override void Configure()
        {
            Delete("connections/{id}");
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

            var connection = await dbContext.Connections
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id && x.WorkspaceId == ws, ct);

            if (connection is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            // Guard: refuse while any workflow's latest version references
            // {{connections.<name>...}} - unless ?force=true.
            if (!Query<bool>("force", isRequired: false))
            {
                var blocking = await ConnectionUsage.FindBlockingWorkflowsAsync(dbContext, connection.Name, ws, ct);
                if (blocking.Count > 0)
                {
                    ThrowError($"Connection '{connection.Name}' is used by the latest version of: "
                        + $"{string.Join(", ", blocking)}. Those workflows will fail to resolve secrets. "
                        + "Pass force=true to delete anyway.");
                }
            }

            await dbContext.Connections
                .Where(x => x.Id == id && x.WorkspaceId == ws)
                .ExecuteDeleteAsync(ct);

            await Send.NoContentAsync(ct);
        }
    }
}
