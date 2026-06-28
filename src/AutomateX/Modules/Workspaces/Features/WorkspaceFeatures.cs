using System.Security.Claims;
using AutomateX.Database;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Workspaces.Features;

public static class GetWorkspaces
{
    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access) : EndpointWithoutRequest<List<Response>>
    {
        public override void Configure()
        {
            Get("workspaces");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            // Fresh invites = my memberships whose subject is still unbound. Captured
            // BEFORE the access checks below bind it — this is the one request where
            // "you've been added" is detectable server-side.
            var email = WorkspaceAccess.GetEmail(User);
            var freshInvites = email is null
                ? []
                : await dbContext.WorkspaceMembers
                    .AsNoTracking()
                    .Where(x => x.Email == email && x.Subject == null)
                    .Select(x => x.WorkspaceId)
                    .ToListAsync(ct);

            // List every workspace the caller can access (membership, unclaimed, or full-access modes).
            var workspaces = await dbContext.Workspaces.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);

            List<Response> accessible = [];
            foreach (var workspace in workspaces)
            {
                if (await access.GetRoleAsync(workspace.Id, User, ct) is { } role)
                {
                    accessible.Add(new Response(workspace.Id, workspace.Name, role.ToString(), freshInvites.Contains(workspace.Id)));
                }
            }

            await Send.OkAsync(accessible, ct);
        }
    }

    public sealed record Response(Guid Id, string Name, string Role, bool IsNew);
}

public static class CreateWorkspace
{
    public sealed class Endpoint(AutomateXDbContext dbContext, Audit.IAuditSink audit) : Endpoint<Request, Response>
    {
        public override void Configure()
        {
            Post("workspaces");
            AllowAnonymous();
        }

        public override async Task HandleAsync(Request req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req.Name))
            {
                ThrowError("Name is required.");
            }

            var workspace = Workspace.Create(req.Name);
            dbContext.Workspaces.Add(workspace);

            // The creator claims ownership immediately (when an identity exists).
            var email = (User.FindFirst("preferred_username") ?? User.FindFirst(ClaimTypes.Email))?.Value;
            if (email is not null)
            {
                var member = WorkspaceMember.Create(workspace.Id, email, WorkspaceRole.Owner);
                var subject = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (subject is not null)
                {
                    member.BindSubject(subject);
                }

                dbContext.WorkspaceMembers.Add(member);
            }

            await dbContext.SaveChangesAsync(ct);
            await audit.RecordAsync(
                "workspace.create", workspace.Id, WorkspaceAccess.GetActor(User),
                "workspace", workspace.Id.ToString(), workspace.Name, ct);
            await Send.OkAsync(new Response(workspace.Id, workspace.Name), ct);
        }
    }

    public sealed record Request(string Name);

    public sealed record Response(Guid Id, string Name);
}

public static class DeleteWorkspace
{
    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access) : EndpointWithoutRequest
    {
        public override void Configure()
        {
            Delete("workspaces/{id}");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            var id = Route<Guid>("id");
            if (id == Workspace.DefaultId)
            {
                ThrowError("The Default workspace cannot be deleted.");
            }

            if (await access.GetRoleAsync(id, User, ct) is not WorkspaceRole.Owner)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            // Cascades: workflows (versions, steps, triggers), connections, executions, members.
            var deleted = await dbContext.Workspaces.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
            if (deleted == 0)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            await Send.NoContentAsync(ct);
        }
    }
}

public static class GetMembers
{
    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access) : EndpointWithoutRequest<List<Response>>
    {
        public override void Configure()
        {
            Get("workspaces/{id}/members");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            var id = Route<Guid>("id");
            if (await access.GetRoleAsync(id, User, ct) is null)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var members = await dbContext.WorkspaceMembers
                .AsNoTracking()
                .Where(x => x.WorkspaceId == id)
                .OrderBy(x => x.Email)
                .Select(x => new Response(x.Id, x.Email, x.Role.ToString(), x.Subject != null))
                .ToListAsync(ct);

            await Send.OkAsync(members, ct);
        }
    }

    public sealed record Response(Guid Id, string Email, string Role, bool SignedInBefore);
}

public static class UpsertMember
{
    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access, Audit.IAuditSink audit) : Endpoint<Request, Response>
    {
        public override void Configure()
        {
            Post("workspaces/{id}/members");
            AllowAnonymous();
        }

        public override async Task HandleAsync(Request req, CancellationToken ct)
        {
            var id = Route<Guid>("id");
            if (await access.GetRoleAsync(id, User, ct) is not WorkspaceRole.Owner)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(req.Email) || !Enum.TryParse<WorkspaceRole>(req.Role, ignoreCase: true, out var role))
            {
                ThrowError("Email and a role (Viewer, Editor, Owner) are required.");
                return;
            }

            var email = req.Email.Trim().ToLowerInvariant();
            var members = await dbContext.WorkspaceMembers.Where(x => x.WorkspaceId == id).ToListAsync(ct);
            var existing = members.FirstOrDefault(x => x.Email == email);

            if (existing is not null)
            {
                if (existing.Role == WorkspaceRole.Owner && role != WorkspaceRole.Owner
                    && !LastOwnerGuard.CanRemoveOrDemote(members, existing))
                {
                    ThrowError("A workspace must keep at least one Owner.");
                }

                existing.ChangeRole(role);
            }
            else
            {
                existing = WorkspaceMember.Create(id, email, role);
                dbContext.WorkspaceMembers.Add(existing);
            }

            await dbContext.SaveChangesAsync(ct);
            await audit.RecordAsync(
                "member.upsert", id, WorkspaceAccess.GetActor(User),
                "member", existing.Id.ToString(), $"{existing.Email} → {existing.Role}", ct);
            await Send.OkAsync(new Response(existing.Id, existing.Email, existing.Role.ToString()), ct);
        }
    }

    public sealed record Request(string? Email, string? Role);

    public sealed record Response(Guid Id, string Email, string Role);
}

public static class RemoveMember
{
    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access, Audit.IAuditSink audit) : EndpointWithoutRequest
    {
        public override void Configure()
        {
            Delete("workspaces/{id}/members/{memberId}");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            var id = Route<Guid>("id");
            var memberId = Route<Guid>("memberId");

            var role = await access.GetRoleAsync(id, User, ct);
            if (role is null)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var members = await dbContext.WorkspaceMembers.Where(x => x.WorkspaceId == id).ToListAsync(ct);
            var target = members.FirstOrDefault(x => x.Id == memberId);
            if (target is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            // Owners manage members; everyone may remove themselves (leave).
            if (role != WorkspaceRole.Owner && !WorkspaceAccess.IsSelf(target, User))
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            if (!LastOwnerGuard.CanRemoveOrDemote(members, target))
            {
                ThrowError("A workspace must keep at least one Owner.");
            }

            dbContext.WorkspaceMembers.Remove(target);
            await dbContext.SaveChangesAsync(ct);
            await audit.RecordAsync(
                "member.remove", id, WorkspaceAccess.GetActor(User), "member", target.Id.ToString(), target.Email, ct);
            await Send.NoContentAsync(ct);
        }
    }
}
