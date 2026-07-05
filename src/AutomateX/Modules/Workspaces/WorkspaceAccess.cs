using System.Security.Claims;
using AutomateX.Database;
using AutomateX.Modules.ApiKeys;
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

    // Scoped API-key awareness (v4.6): when a request authenticated via an atx_ key, its ApiKeyContext
    // is authoritative and short-circuits the OIDC/open logic. Instance keys are Owner everywhere and
    // instance admins; a Workspace key is its Role but only within its own workspace — an X-Workspace-Id
    // naming another workspace is refused, so the header can never widen a scoped key.
    public async Task<WorkspaceRole?> GetRoleAsync(Guid workspaceId, HttpContext context, CancellationToken cancellationToken)
    {
        if (ApiKeyContext.From(context) is { } key)
        {
            return key.Scope != ApiKeyScope.Instance && key.WorkspaceId != workspaceId ? null : RoleForKey(key);
        }

        return await GetRoleAsync(workspaceId, context.User, cancellationToken);
    }

    private static WorkspaceRole RoleForKey(ApiKeyContext key) =>
        key.Scope == ApiKeyScope.Instance ? WorkspaceRole.Owner : key.Role;

    // The single workflow a Workflow-scoped key is confined to; null for any other caller. Fold this into
    // the WorkspaceId filter of list/entity queries so a workflow key can't see or touch other workflows.
    public static Guid? ScopedWorkflowId(HttpContext context) =>
        ApiKeyContext.From(context) is { Scope: ApiKeyScope.Workflow } key ? key.WorkflowId : null;

    // The workspace a key may act on: an Instance key acts on whatever is addressed; a scoped key is
    // pinned to its own, and an X-Workspace-Id header naming a different workspace returns null (403).
    private static Guid? EffectiveWorkspaceForKey(ApiKeyContext key, HttpContext context)
    {
        if (key.Scope == ApiKeyScope.Instance)
        {
            return WorkspaceHttp.GetWorkspaceId(context);
        }

        var requested = context.Request.Headers[WorkspaceHttp.HeaderName].FirstOrDefault();
        return requested is not null && Guid.TryParse(requested, out var target) && target != key.WorkspaceId
            ? null
            : key.WorkspaceId;
    }

    public static string? GetSubject(ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;

    public static string? GetEmail(ClaimsPrincipal user) =>
        (user.FindFirst("preferred_username") ?? user.FindFirst(ClaimTypes.Email) ?? user.FindFirst("email"))
            ?.Value.ToLowerInvariant();

    // The audit/actor identity: a scoped key ("key:<name>") when present, else the OIDC subject/email,
    // else a machine/open-instance caller.
    public static string GetActor(HttpContext context) =>
        ApiKeyContext.From(context)?.Actor ?? GetActor(context.User);

    public static string GetActor(ClaimsPrincipal user) =>
        GetSubject(user) ?? GetEmail(user) ?? "api-key";

    // A scoped key is an instance admin only when its Scope is Instance; otherwise defer to the principal.
    public bool IsInstanceAdmin(HttpContext context) =>
        ApiKeyContext.From(context) is { } key ? key.Scope == ApiKeyScope.Instance : IsInstanceAdmin(context.User);

    // Instance operator: above workspace-owner, sees across workspaces. In open/api-key mode the
    // caller through the gate is the operator; in OIDC mode they must be listed in Auth__InstanceAdmins
    // (by subject or email). Never inferred from workspace data.
    public bool IsInstanceAdmin(ClaimsPrincipal user)
    {
        if (!authOptions.Value.OidcConfigured)
        {
            return true;
        }

        // A machine client (api-key through the gate, no OIDC identity) is the instance master
        // credential — treat it as an operator, consistent with it getting Owner everywhere.
        if (user.Identity?.IsAuthenticated != true)
        {
            return true;
        }

        var admins = authOptions.Value.InstanceAdmins;
        if (admins.Count == 0)
        {
            return false;
        }

        var subject = GetSubject(user);
        var email = GetEmail(user);
        return admins.Any(admin =>
            (subject is not null && string.Equals(admin, subject, StringComparison.Ordinal))
            || (email is not null && string.Equals(admin, email, StringComparison.OrdinalIgnoreCase)));
    }

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

    // Convenience for endpoints: resolves the effective workspace and checks the minimum role. A key
    // context is authoritative (and never touches the DB); otherwise the header workspace + OIDC/open path.
    public async Task<Guid?> AuthorizeAsync(HttpContext context, WorkspaceRole required, CancellationToken cancellationToken)
    {
        if (ApiKeyContext.From(context) is { } key)
        {
            return EffectiveWorkspaceForKey(key, context) is { } keyWorkspace && RoleForKey(key) >= required
                ? keyWorkspace
                : null;
        }

        var workspaceId = WorkspaceHttp.GetWorkspaceId(context);
        var role = await GetRoleAsync(workspaceId, context.User, cancellationToken);
        return role is { } r && r >= required ? workspaceId : null;
    }

    // Workflow-addressed endpoints: the workspace role check plus, for a Workflow-scoped key, a hard gate
    // that the addressed workflow is the key's one. Returns the effective workspace, or null (403).
    public async Task<Guid?> AuthorizeWorkflowAsync(
        HttpContext context, Guid workflowId, WorkspaceRole required, CancellationToken cancellationToken)
    {
        if (await AuthorizeAsync(context, required, cancellationToken) is not { } workspaceId)
        {
            return null;
        }

        return ScopedWorkflowId(context) is { } only && only != workflowId ? null : workspaceId;
    }
}
