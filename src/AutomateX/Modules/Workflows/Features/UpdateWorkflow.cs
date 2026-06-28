using System.Text.Json;
using AutomateX.Database;
using AutomateX.Engine.Actions;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Workflows.Features;

public static class UpdateWorkflow
{
    public sealed class Endpoint(
        AutomateXDbContext dbContext, ActionRegistry actions, WorkspaceAccess access, Audit.IAuditSink audit) : Endpoint<Request, Response>
    {
        public override void Configure()
        {
            Put("workflows/{id}");
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
                ThrowError("Name is required.");
            }

            foreach (var step in req.Steps.Where(step => !actions.Contains(step.ActionType, ws)))
            {
                ThrowError($"Unknown action type '{step.ActionType}'.");
            }

            var workflow = await dbContext.Workflows
                .Include(x => x.Versions)
                .FirstOrDefaultAsync(x => x.Id == req.Id && x.WorkspaceId == ws, ct);

            if (workflow is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            var edges = CreateWorkflow.BuildEdges(req.Edges, req.Steps.Count, message => ThrowError(message));

            var steps = req.Steps
                .Select(x => new StepDefinition(x.ActionType, x.Name, x.Config.GetRawText(), IdempotencyKey: x.IdempotencyKey))
                .ToList();
            StepReferences.Validate(steps, message => ThrowError(message));

            workflow.Rename(req.Name, req.Description);
            var version = workflow.AddVersion(steps, edges, req.ContinueOnFailure);

            // Explicit Add — see ExecuteStepHandler: discovered children with client-set keys track as Modified.
            dbContext.WorkflowVersions.Add(version);

            await dbContext.SaveChangesAsync(ct);

            await audit.RecordAsync(
                "workflow.update", ws, WorkspaceAccess.GetActor(User),
                "workflow", workflow.Id.ToString(), $"v{version.Version}", ct);

            await Send.OkAsync(new Response(workflow.Id, version.Id, version.Version), ct);
        }
    }

    public sealed record Request(
        Guid Id, string Name, string? Description, List<CreateWorkflow.StepRequest> Steps,
        List<CreateWorkflow.EdgeRequest>? Edges = null, bool ContinueOnFailure = false);

    public sealed record Response(Guid Id, Guid VersionId, int Version);
}
