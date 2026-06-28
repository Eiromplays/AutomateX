using AutomateX.Database;
using AutomateX.Engine.Security;
using AutomateX.Modules.Audit;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Variables.Features;

public static class GetVariables
{
    // Names, scope, secret flag, and which environments have a value — never the values themselves.
    public sealed record VariableResponse(Guid Id, string Name, bool Secret, Guid? WorkflowId, List<Guid> EnvironmentIds);

    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access)
        : EndpointWithoutRequest<List<VariableResponse>>
    {
        public override void Configure()
        {
            Get("variables");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Viewer, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var workflowId = Query<Guid?>("workflowId", isRequired: false);

            var variables = await dbContext.Variables
                .AsNoTracking()
                .Where(x => x.WorkspaceId == ws && (x.WorkflowId == null || x.WorkflowId == workflowId))
                .OrderBy(x => x.Name)
                .Select(x => new VariableResponse(
                    x.Id, x.Name, x.Secret, x.WorkflowId, x.Values.Select(v => v.EnvironmentId).ToList()))
                .ToListAsync(ct);

            await Send.OkAsync(variables, ct);
        }
    }
}

public static class CreateVariable
{
    public sealed record Request(string? Name, bool Secret, Guid? WorkflowId);

    public sealed record Response(Guid Id, string Name, bool Secret, Guid? WorkflowId);

    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access, IAuditSink audit)
        : Endpoint<Request, Response>
    {
        public override void Configure()
        {
            Post("variables");
            AllowAnonymous();
        }

        public override async Task HandleAsync(Request req, CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Editor, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            if (!VariableNames.Pattern().IsMatch(req.Name ?? ""))
            {
                ThrowError("Variable name may contain letters, digits, '-' and '_' (max 64) — it is used in {{vars.<name>}}.");
            }

            if (req.WorkflowId is { } wf && !await dbContext.Workflows.AnyAsync(x => x.Id == wf && x.WorkspaceId == ws, ct))
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            if (await dbContext.Variables.AnyAsync(
                x => x.WorkspaceId == ws && x.WorkflowId == req.WorkflowId && x.Name == req.Name, ct))
            {
                ThrowError($"A variable named '{req.Name}' already exists in this scope.");
            }

            var variable = Variable.Create(ws, req.WorkflowId, req.Name!, req.Secret);
            dbContext.Variables.Add(variable);
            await dbContext.SaveChangesAsync(ct);

            await audit.RecordAsync(
                "variable.create", ws, WorkspaceAccess.GetActor(User), "variable", variable.Id.ToString(), variable.Name, ct);
            await Send.OkAsync(new Response(variable.Id, variable.Name, variable.Secret, variable.WorkflowId), ct);
        }
    }
}

public static class DeleteVariable
{
    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access, IAuditSink audit) : EndpointWithoutRequest
    {
        public override void Configure()
        {
            Delete("variables/{id}");
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
            var variable = await dbContext.Variables.FirstOrDefaultAsync(x => x.Id == id && x.WorkspaceId == ws, ct);
            if (variable is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            dbContext.Variables.Remove(variable); // cascades its values
            await dbContext.SaveChangesAsync(ct);

            await audit.RecordAsync(
                "variable.delete", ws, WorkspaceAccess.GetActor(User), "variable", id.ToString(), variable.Name, ct);
            await Send.NoContentAsync(ct);
        }
    }
}

public static class SetVariableValue
{
    public sealed record Request(Guid EnvironmentId, string? Value);

    public sealed class Endpoint(AutomateXDbContext dbContext, TenantCipher cipher, WorkspaceAccess access, IAuditSink audit)
        : Endpoint<Request>
    {
        public override void Configure()
        {
            Put("variables/{id}/values");
            AllowAnonymous();
        }

        public override async Task HandleAsync(Request req, CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Editor, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var id = Route<Guid>("id");
            var variable = await dbContext.Variables
                .Include(x => x.Values)
                .FirstOrDefaultAsync(x => x.Id == id && x.WorkspaceId == ws, ct);
            if (variable is null
                || !await dbContext.WorkspaceEnvironments.AnyAsync(x => x.Id == req.EnvironmentId && x.WorkspaceId == ws, ct))
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            string stored;
            try
            {
                stored = variable.Secret ? await cipher.EncryptAsync(req.Value ?? "", ws, ct) : req.Value ?? "";
            }
            catch (SecretCipherException ex)
            {
                ThrowError(ex.Message);
                return;
            }

            var existing = variable.Values.FirstOrDefault(v => v.EnvironmentId == req.EnvironmentId);
            if (existing is null)
            {
                variable.Values.Add(VariableValue.Create(variable.Id, req.EnvironmentId, stored));
            }
            else
            {
                existing.SetValue(stored);
            }

            await dbContext.SaveChangesAsync(ct);

            await audit.RecordAsync(
                "variable.value.set", ws, WorkspaceAccess.GetActor(User), "variable", variable.Id.ToString(), variable.Name, ct);
            await Send.NoContentAsync(ct);
        }
    }
}
