using AutomateX.Database;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Workflows.Features;

public static class GetWorkflows
{
    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access) : EndpointWithoutRequest<List<Response>>
    {
        public override void Configure()
        {
            Get("workflows");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Viewer, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var workflows = await dbContext.Workflows
                .AsNoTracking()
                .Where(x => x.WorkspaceId == ws)
                .OrderBy(x => x.Name)
                .Select(x => new Response(
                    x.Id,
                    x.Name,
                    x.Description,
                    x.CreatedAt,
                    x.Versions.Max(v => v.Version)))
                .ToListAsync(ct);

            await Send.OkAsync(workflows, ct);
        }
    }

    public sealed record Response(Guid Id, string Name, string? Description, DateTimeOffset CreatedAt, int LatestVersion);
}
