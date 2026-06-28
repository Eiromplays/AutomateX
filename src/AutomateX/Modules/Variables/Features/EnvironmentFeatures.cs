using System.Text.RegularExpressions;
using AutomateX.Database;
using AutomateX.Modules.Audit;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Variables.Features;

internal static partial class VariableNames
{
    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    public static partial Regex Pattern();
}

internal static class EnvironmentBootstrap
{
    // Lazily give a workspace a 'default' environment so variable values always have somewhere to live.
    public static async Task EnsureDefaultAsync(AutomateXDbContext dbContext, Guid workspaceId, CancellationToken ct)
    {
        if (!await dbContext.WorkspaceEnvironments.AnyAsync(x => x.WorkspaceId == workspaceId, ct))
        {
            dbContext.WorkspaceEnvironments.Add(WorkspaceEnvironment.Create(workspaceId, WorkspaceEnvironment.DefaultName));
            await dbContext.SaveChangesAsync(ct);
        }
    }
}

public static class GetEnvironments
{
    public sealed record EnvironmentResponse(Guid Id, string Name, bool Active);

    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access)
        : EndpointWithoutRequest<List<EnvironmentResponse>>
    {
        public override void Configure()
        {
            Get("environments");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Viewer, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            await EnvironmentBootstrap.EnsureDefaultAsync(dbContext, ws, ct);

            var active = await dbContext.Workspaces
                .Where(x => x.Id == ws)
                .Select(x => x.ActiveEnvironmentId)
                .FirstOrDefaultAsync(ct);

            var environments = await dbContext.WorkspaceEnvironments
                .AsNoTracking()
                .Where(x => x.WorkspaceId == ws)
                .OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Name })
                .ToListAsync(ct);

            // No explicit active = the 'default' environment is active.
            var activeId = active ?? environments.FirstOrDefault(e => e.Name == WorkspaceEnvironment.DefaultName)?.Id;
            await Send.OkAsync(
                environments.Select(e => new EnvironmentResponse(e.Id, e.Name, e.Id == activeId)).ToList(), ct);
        }
    }
}

public static class CreateEnvironment
{
    public sealed record Request(string? Name);

    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access, IAuditSink audit)
        : Endpoint<Request, GetEnvironments.EnvironmentResponse>
    {
        public override void Configure()
        {
            Post("environments");
            AllowAnonymous();
        }

        public override async Task HandleAsync(Request req, CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Owner, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            if (!VariableNames.Pattern().IsMatch(req.Name ?? ""))
            {
                ThrowError("Environment name may contain letters, digits, '-' and '_' (max 64).");
            }

            if (await dbContext.WorkspaceEnvironments.AnyAsync(x => x.WorkspaceId == ws && x.Name == req.Name, ct))
            {
                ThrowError($"An environment named '{req.Name}' already exists.");
            }

            var environment = WorkspaceEnvironment.Create(ws, req.Name!);
            dbContext.WorkspaceEnvironments.Add(environment);
            await dbContext.SaveChangesAsync(ct);

            await audit.RecordAsync(
                "environment.create", ws, WorkspaceAccess.GetActor(User), "environment", environment.Id.ToString(), environment.Name, ct);

            await Send.OkAsync(new GetEnvironments.EnvironmentResponse(environment.Id, environment.Name, Active: false), ct);
        }
    }
}

public static class DeleteEnvironment
{
    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access, IAuditSink audit) : EndpointWithoutRequest
    {
        public override void Configure()
        {
            Delete("environments/{id}");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Owner, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var id = Route<Guid>("id");
            var environment = await dbContext.WorkspaceEnvironments
                .FirstOrDefaultAsync(x => x.Id == id && x.WorkspaceId == ws, ct);
            if (environment is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            if (await dbContext.WorkspaceEnvironments.CountAsync(x => x.WorkspaceId == ws, ct) <= 1)
            {
                ThrowError("Can't delete the last environment.");
            }

            // If it was the active one, fall back to no explicit active (the 'default' environment).
            var workspace = await dbContext.Workspaces.FirstAsync(x => x.Id == ws, ct);
            if (workspace.ActiveEnvironmentId == id)
            {
                workspace.SetActiveEnvironment(null);
            }

            dbContext.WorkspaceEnvironments.Remove(environment); // cascades its VariableValues
            await dbContext.SaveChangesAsync(ct);

            await audit.RecordAsync(
                "environment.delete", ws, WorkspaceAccess.GetActor(User), "environment", id.ToString(), environment.Name, ct);
            await Send.NoContentAsync(ct);
        }
    }
}

public static class SetActiveEnvironment
{
    public sealed record Request(Guid EnvironmentId);

    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access, IAuditSink audit)
        : Endpoint<Request>
    {
        public override void Configure()
        {
            Put("environments/active");
            AllowAnonymous();
        }

        public override async Task HandleAsync(Request req, CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Owner, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var environment = await dbContext.WorkspaceEnvironments
                .FirstOrDefaultAsync(x => x.Id == req.EnvironmentId && x.WorkspaceId == ws, ct);
            if (environment is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            var workspace = await dbContext.Workspaces.FirstAsync(x => x.Id == ws, ct);
            workspace.SetActiveEnvironment(req.EnvironmentId);
            await dbContext.SaveChangesAsync(ct);

            await audit.RecordAsync(
                "environment.activate", ws, WorkspaceAccess.GetActor(User), "environment", environment.Id.ToString(), environment.Name, ct);
            await Send.NoContentAsync(ct);
        }
    }
}
