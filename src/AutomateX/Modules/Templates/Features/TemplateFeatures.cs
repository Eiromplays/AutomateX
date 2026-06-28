using System.Text.Json.Nodes;
using AutomateX.Database;
using AutomateX.Modules.Audit;
using AutomateX.Modules.Workflows;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Templates.Features;

public static class GetTemplates
{
    public sealed record Response(Guid Id, string Name, string? Description, string? Category, JsonNode? Doc, DateTimeOffset CreatedAt);

    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access) : EndpointWithoutRequest<List<Response>>
    {
        public override void Configure()
        {
            Get("templates");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Viewer, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var templates = await dbContext.WorkflowTemplates
                .AsNoTracking()
                .Where(x => x.WorkspaceId == ws)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new { x.Id, x.Name, x.Description, x.Category, x.Doc, x.CreatedAt })
                .ToListAsync(ct);

            await Send.OkAsync(
                templates
                    .Select(x => new Response(x.Id, x.Name, x.Description, x.Category, ParseOrNull(x.Doc), x.CreatedAt))
                    .ToList(),
                ct);
        }

        private static JsonNode? ParseOrNull(string doc)
        {
            try
            {
                return JsonNode.Parse(doc);
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }
    }
}

public static class SaveTemplate
{
    public sealed record Request(string? Name, string? Description, string? Category, Guid FromWorkflowId);

    public sealed record Response(Guid Id, string Name);

    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access, IAuditSink audit)
        : Endpoint<Request, Response>
    {
        public override void Configure()
        {
            Post("templates");
            AllowAnonymous();
        }

        public override async Task HandleAsync(Request req, CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Editor, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(req.Name))
            {
                ThrowError("A template name is required.");
            }

            var workflow = await dbContext.Workflows
                .AsNoTracking()
                .Include(x => x.Versions).ThenInclude(x => x.Steps)
                .Include(x => x.Versions).ThenInclude(x => x.Edges)
                .FirstOrDefaultAsync(x => x.Id == req.FromWorkflowId && x.WorkspaceId == ws, ct);
            if (workflow is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            var latest = workflow.Versions.OrderByDescending(x => x.Version).First();
            var steps = latest.Steps
                .OrderBy(x => x.Order)
                .Select(x => new StepDefinition(x.ActionType, x.Name, x.ConfigJson, x.Key, x.IdempotencyKey))
                .ToList();
            var edges = latest.Edges.Select(x => new EdgeDefinition(x.FromOrder, x.ToOrder, x.Label)).ToList();
            var triggers = await dbContext.Triggers
                .AsNoTracking()
                .Where(x => x.WorkflowId == req.FromWorkflowId)
                .Select(x => new { x.Type, x.ConfigJson })
                .ToListAsync(ct);

            var doc = WorkflowTransfer.Export(
                workflow.Name, workflow.Description, steps,
                triggers.Select(x => (x.Type, x.ConfigJson)).ToList(), edges, latest.ContinueOnFailure);

            var template = WorkflowTemplate.Create(ws, req.Name!, req.Description, req.Category, doc.ToJsonString());
            dbContext.WorkflowTemplates.Add(template);
            await dbContext.SaveChangesAsync(ct);

            await audit.RecordAsync(
                "template.create", ws, WorkspaceAccess.GetActor(User), "template", template.Id.ToString(), template.Name, ct);
            await Send.OkAsync(new Response(template.Id, template.Name), ct);
        }
    }
}

public static class DeleteTemplate
{
    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access, IAuditSink audit) : EndpointWithoutRequest
    {
        public override void Configure()
        {
            Delete("templates/{id}");
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
            var template = await dbContext.WorkflowTemplates.FirstOrDefaultAsync(x => x.Id == id && x.WorkspaceId == ws, ct);
            if (template is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            dbContext.WorkflowTemplates.Remove(template);
            await dbContext.SaveChangesAsync(ct);

            await audit.RecordAsync(
                "template.delete", ws, WorkspaceAccess.GetActor(User), "template", id.ToString(), template.Name, ct);
            await Send.NoContentAsync(ct);
        }
    }
}
