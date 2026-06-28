using System.Text.Json;
using AutomateX.Database;
using AutomateX.Engine.Actions;
using AutomateX.Modules.Workspaces;
using FastEndpoints;

namespace AutomateX.Modules.Workflows.Features;

public static class CreateWorkflow
{
    public sealed class Endpoint(
        AutomateXDbContext dbContext, ActionRegistry actions, WorkspaceAccess access, Audit.IAuditSink audit) : Endpoint<Request, Response>
    {
        public override void Configure()
        {
            Post("workflows");
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

            var edges = BuildEdges(req.Edges, req.Steps.Count, message => ThrowError(message));

            var steps = req.Steps
                .Select(x => new StepDefinition(x.ActionType, x.Name, x.Config.GetRawText(), IdempotencyKey: x.IdempotencyKey))
                .ToList();
            StepReferences.Validate(steps, message => ThrowError(message));

            var workflow = Workflow.Create(req.Name, req.Description, ws);
            var version = workflow.AddVersion(steps, edges, req.ContinueOnFailure);

            dbContext.Workflows.Add(workflow);
            await dbContext.SaveChangesAsync(ct);

            await audit.RecordAsync(
                "workflow.create", ws, WorkspaceAccess.GetActor(User),
                "workflow", workflow.Id.ToString(), workflow.Name, ct);

            await Send.OkAsync(new Response(workflow.Id, version.Id, version.Version), ct);
        }
    }

    public sealed record Request(
        string Name, string? Description, List<StepRequest> Steps, List<EdgeRequest>? Edges = null,
        bool ContinueOnFailure = false);

    public sealed record StepRequest(string ActionType, string? Name, JsonElement Config, string? IdempotencyKey = null);

    public sealed record EdgeRequest(int From, int To, string? Label);

    public sealed record Response(Guid Id, Guid VersionId, int Version);

    // Maps edge requests to definitions, rejecting any that point outside the step set.
    // A blank label is normalised to null (an unconditional link).
    internal static List<EdgeDefinition> BuildEdges(IReadOnlyList<EdgeRequest>? edges, int stepCount, Action<string> fail)
    {
        List<EdgeDefinition> result = [];
        HashSet<int> errorSources = [];
        foreach (var edge in edges ?? [])
        {
            if (edge.From < 0 || edge.From >= stepCount || edge.To < 0 || edge.To >= stepCount)
            {
                fail($"Edge {edge.From}->{edge.To} references a step that doesn't exist.");
            }

            var label = string.IsNullOrWhiteSpace(edge.Label) ? null : edge.Label;

            // "error" is the reserved failure-path label: a step can have at most one error edge.
            if (label == AutomateX.Engine.Edges.ErrorLabel && !errorSources.Add(edge.From))
            {
                fail($"Step {edge.From} has more than one error edge — only one is allowed.");
            }

            result.Add(new EdgeDefinition(edge.From, edge.To, label));
        }

        return result;
    }
}
