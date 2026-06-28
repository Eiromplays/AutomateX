using AutomateX.Database;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Audit.Features;

public static class GetAudit
{
    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access)
        : EndpointWithoutRequest<List<Response>>
    {
        public override void Configure()
        {
            Get("audit");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            var requestedWorkspace = WorkspaceHttp.GetWorkspaceId(HttpContext);
            var isAdmin = access.IsInstanceAdmin(User);

            // Admins read across workspaces (optionally filtered by ?workspaceId); members read only
            // their own workspace, and only with at least Viewer there.
            Guid? scope;
            if (isAdmin)
            {
                scope = Guid.TryParse(Query<string?>("workspaceId", isRequired: false), out var filter) ? filter : null;
            }
            else
            {
                if (await access.GetRoleAsync(requestedWorkspace, User, ct) is null)
                {
                    await Send.ForbiddenAsync(ct);
                    return;
                }

                scope = requestedWorkspace;
            }

            var take = Math.Clamp(Query<int?>("take", isRequired: false) ?? 100, 1, 500);
            var since = Query<DateTimeOffset?>("since", isRequired: false);

            var rows = await AuditQuery.Apply(
                    dbContext.AuditEntries.AsNoTracking(),
                    scope,
                    Query<string?>("actor", isRequired: false),
                    Query<string?>("action", isRequired: false),
                    Query<string?>("targetType", isRequired: false),
                    since)
                .Take(take)
                .Select(x => new Response(
                    x.Id, x.At, x.Actor, x.WorkspaceId, x.Action, x.TargetType, x.TargetId, x.Summary))
                .ToListAsync(ct);

            await Send.OkAsync(rows, ct);
        }
    }

    public sealed record Response(
        Guid Id,
        DateTimeOffset At,
        string Actor,
        Guid? WorkspaceId,
        string Action,
        string? TargetType,
        string? TargetId,
        string? Summary);
}
