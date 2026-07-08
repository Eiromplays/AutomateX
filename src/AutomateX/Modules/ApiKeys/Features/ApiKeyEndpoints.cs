using AutomateX.Database;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.ApiKeys.Features;

// Mint a scoped key. The full token is returned exactly once here and never again — only its hash is
// stored. Instance keys require an instance admin; Workspace/Workflow keys require the caller be Owner of
// the target workspace, at a role no higher than their own.
public static class CreateApiKey
{
    public sealed class Endpoint(
        AutomateXDbContext dbContext, WorkspaceAccess access, ApiKeyPresence presence, Audit.IAuditSink audit)
        : Endpoint<Request, Response>
    {
        public override void Configure()
        {
            Post("keys");
            AllowAnonymous();
        }

        public override async Task HandleAsync(Request req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req.Name))
            {
                ThrowError("Name is required.");
                return;
            }

            if (!Enum.TryParse<ApiKeyScope>(req.Scope, ignoreCase: true, out var scope) || !Enum.IsDefined(scope))
            {
                ThrowError("Scope must be Instance, Workspace or Workflow.");
                return;
            }

            var role = WorkspaceRole.Viewer;
            if (!string.IsNullOrWhiteSpace(req.Role)
                && (!Enum.TryParse(req.Role, ignoreCase: true, out role) || !Enum.IsDefined(role)))
            {
                ThrowError("Role must be Viewer, Editor or Owner.");
                return;
            }

            if (scope is ApiKeyScope.Workspace or ApiKeyScope.Workflow && req.WorkspaceId is null)
            {
                ThrowError("workspaceId is required for a Workspace- or Workflow-scoped key.");
                return;
            }

            if (scope == ApiKeyScope.Workflow && req.WorkflowId is null)
            {
                ThrowError("workflowId is required for a Workflow-scoped key.");
                return;
            }

            if (req.RateLimitPerMinute is { } rateLimit && rateLimit < 1)
            {
                ThrowError("rateLimitPerMinute must be a positive number.");
                return;
            }

            var ipAllowlist = req.IpAllowlist ?? [];
            if (ClientIpAllowlist.FindInvalid(ipAllowlist) is { Count: > 0 } invalid)
            {
                ThrowError($"ipAllowlist has invalid entries: {string.Join(", ", invalid)}");
                return;
            }

            var isAdmin = access.IsInstanceAdmin(HttpContext);
            var myRole = isAdmin || scope == ApiKeyScope.Instance
                ? (WorkspaceRole?)null
                : await access.GetRoleAsync(req.WorkspaceId!.Value, HttpContext, ct);

            if (!ApiKeyMintPolicy.CanMint(scope, role, isAdmin, myRole))
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            if (scope == ApiKeyScope.Workspace
                && !await dbContext.Workspaces.AnyAsync(x => x.Id == req.WorkspaceId, ct))
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            if (scope == ApiKeyScope.Workflow
                && !await dbContext.Workflows.AnyAsync(x => x.Id == req.WorkflowId && x.WorkspaceId == req.WorkspaceId, ct))
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            var (token, prefix, hash) = ApiKeyToken.Mint();
            var key = ApiKey.Create(
                req.Name.Trim(),
                prefix,
                hash,
                scope,
                scope == ApiKeyScope.Instance ? null : req.WorkspaceId,
                scope == ApiKeyScope.Workflow ? req.WorkflowId : null,
                role,
                WorkspaceAccess.GetActor(HttpContext),
                req.ExpiresAt,
                req.RateLimitPerMinute,
                ipAllowlist);

            dbContext.ApiKeys.Add(key);
            await dbContext.SaveChangesAsync(ct);
            presence.Invalidate(); // the instance now has (at least) this key

            await audit.RecordAsync(
                "apikey.create", key.WorkspaceId, WorkspaceAccess.GetActor(HttpContext),
                "apikey", key.Id.ToString(), $"{key.Name} ({key.Scope})", ct);

            await Send.OkAsync(
                new Response(
                    key.Id, key.Name, key.Prefix, key.Scope.ToString(), key.WorkspaceId, key.WorkflowId,
                    key.Role.ToString(), key.ExpiresAt, key.RateLimitPerMinute, key.IpAllowlist, token),
                ct);
        }
    }

    public sealed record Request(
        string? Name, string? Scope, Guid? WorkspaceId, Guid? WorkflowId, string? Role, DateTimeOffset? ExpiresAt,
        int? RateLimitPerMinute = null, string[]? IpAllowlist = null);

    // Token is the full one-time credential; every other field is safe to keep.
    public sealed record Response(
        Guid Id, string Name, string Prefix, string Scope, Guid? WorkspaceId, Guid? WorkflowId,
        string Role, DateTimeOffset? ExpiresAt, int? RateLimitPerMinute, string[] IpAllowlist, string Token);
}

// List keys (metadata only — never a secret or hash). Instance admins see every key; a workspace Owner
// sees their current workspace's (X-Workspace-Id). Revoked keys stay listed with their RevokedAt.
public static class GetApiKeys
{
    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access)
        : EndpointWithoutRequest<List<Response>>
    {
        public override void Configure()
        {
            Get("keys");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            var query = dbContext.ApiKeys.AsNoTracking();

            if (!access.IsInstanceAdmin(HttpContext))
            {
                var workspaceId = WorkspaceHttp.GetWorkspaceId(HttpContext);
                if (await access.GetRoleAsync(workspaceId, HttpContext, ct) is not WorkspaceRole.Owner)
                {
                    await Send.ForbiddenAsync(ct);
                    return;
                }

                query = query.Where(x => x.WorkspaceId == workspaceId);
            }

            var keys = await query
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new Response(
                    x.Id, x.Name, x.Prefix, x.Scope.ToString(), x.WorkspaceId, x.WorkflowId, x.Role.ToString(),
                    x.CreatedBy, x.CreatedAt, x.LastUsedAt, x.ExpiresAt, x.RevokedAt, x.RateLimitPerMinute, x.IpAllowlist))
                .ToListAsync(ct);

            await Send.OkAsync(keys, ct);
        }
    }

    public sealed record Response(
        Guid Id, string Name, string Prefix, string Scope, Guid? WorkspaceId, Guid? WorkflowId, string Role,
        string CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, DateTimeOffset? ExpiresAt,
        DateTimeOffset? RevokedAt, int? RateLimitPerMinute, string[] IpAllowlist);
}

// Soft-revoke a key (the row survives for audit). Same authz as minting; idempotent.
public static class RevokeApiKey
{
    public sealed class Endpoint(
        AutomateXDbContext dbContext, WorkspaceAccess access, ApiKeyPresence presence, Audit.IAuditSink audit)
        : EndpointWithoutRequest
    {
        public override void Configure()
        {
            Delete("keys/{id}");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            var id = Route<Guid>("id");
            var key = await dbContext.ApiKeys.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (key is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            if (!access.IsInstanceAdmin(HttpContext)
                && (key.Scope == ApiKeyScope.Instance
                    || key.WorkspaceId is not { } ws
                    || await access.GetRoleAsync(ws, HttpContext, ct) is not WorkspaceRole.Owner))
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            if (key.RevokedAt is null)
            {
                key.Revoke();
                await dbContext.SaveChangesAsync(ct);
                presence.Invalidate(); // this may have been the last active key

                await audit.RecordAsync(
                    "apikey.revoke", key.WorkspaceId, WorkspaceAccess.GetActor(HttpContext),
                    "apikey", key.Id.ToString(), key.Name, ct);
            }

            await Send.NoContentAsync(ct);
        }
    }
}
