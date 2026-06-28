using System.Security.Claims;
using AutomateX.Database;
using AutomateX.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutomateX.Modules.Workspaces;

public static class WorkspaceHttp
{
    public const string HeaderName = "X-Workspace-Id";

    public static Guid GetWorkspaceId(HttpContext context) =>
        Guid.TryParse(context.Request.Headers[HeaderName].FirstOrDefault(), out var id)
            ? id
            : Workspace.DefaultId;
}

// Role resolution rules (encoded in WorkspaceAccessTests):
// open/apikey modes → Owner; OIDC machine clients (key through the gate) → Owner;
// zero-member workspace → Owner for any authenticated user (bootstrap);
// members match by bound subject, then email (binding the subject on first match).
public sealed class WorkspaceAccess(AutomateXDbContext dbContext, IOptions<AuthOptions> authOptions)
{
    public async Task<WorkspaceRole?> GetRoleAsync(Guid workspaceId, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!authOptions.Value.OidcConfigured || user.Identity?.IsAuthenticated != true)
        {
            return WorkspaceRole.Owner;
        }

        var subject = GetSubject(user);
        var email = GetEmail(user);

        var members = await dbContext.WorkspaceMembers
            .Where(x => x.WorkspaceId == workspaceId)
            .ToListAsync(cancellationToken);

        if (members.Count == 0)
        {
            return WorkspaceRole.Owner;
        }

        var member = members.FirstOrDefault(x => subject is not null && x.Subject == subject)
            ?? members.FirstOrDefault(x => email is not null && x.Email == email);

        if (member is null)
        {
            return null;
        }

        if (member.Subject is null && subject is not null)
        {
            member.BindSubject(subject);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return member.Role;
    }

    public static string? GetSubject(ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;

    public static string? GetEmail(ClaimsPrincipal user) =>
        (user.FindFirst("preferred_username") ?? user.FindFirst(ClaimTypes.Email) ?? user.FindFirst("email"))
            ?.Value.ToLowerInvariant();

    // The audit/actor identity: the OIDC subject, then email, else a machine/open-instance caller.
    public static string GetActor(ClaimsPrincipal user) =>
        GetSubject(user) ?? GetEmail(user) ?? "api-key";

    // Self-identification for member self-service (e.g. leaving a workspace).
    public static bool IsSelf(WorkspaceMember member, ClaimsPrincipal user)
    {
        var subject = GetSubject(user);
        if (subject is not null && member.Subject == subject)
        {
            return true;
        }

        var email = GetEmail(user);
        return email is not null && member.Email == email;
    }

    // Convenience for endpoints: resolves the header workspace and checks the minimum role.
    public async Task<Guid?> AuthorizeAsync(HttpContext context, WorkspaceRole required, CancellationToken cancellationToken)
    {
        var workspaceId = WorkspaceHttp.GetWorkspaceId(context);
        var role = await GetRoleAsync(workspaceId, context.User, cancellationToken);
        return role is { } r && r >= required ? workspaceId : null;
    }
}
