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

            var deleted = await dbContext.Connections
                .Where(x => x.Id == id && x.WorkspaceId == ws)
                .ExecuteDeleteAsync(ct);

            if (deleted == 0)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            await Send.NoContentAsync(ct);
        }
    }
}
