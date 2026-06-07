using System.Text.Json;
using AutomateX.Database;
using AutomateX.Engine.Actions;
using AutomateX.Modules.Workspaces;
using FastEndpoints;

namespace AutomateX.Modules.Workflows.Features;

public static class CreateWorkflow
{
    public sealed class Endpoint(AutomateXDbContext dbContext, ActionRegistry actions, WorkspaceAccess access) : Endpoint<Request, Response>
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

            var workflow = Workflow.Create(req.Name, req.Description, ws);
            var version = workflow.AddVersion(req.Steps
                .Select(x => new StepDefinition(x.ActionType, x.Name, x.Config.GetRawText()))
                .ToList());

            dbContext.Workflows.Add(workflow);
            await dbContext.SaveChangesAsync(ct);

            await Send.OkAsync(new Response(workflow.Id, version.Id, version.Version), ct);
        }
    }

    public sealed record Request(string Name, string? Description, List<StepRequest> Steps);

    public sealed record StepRequest(string ActionType, string? Name, JsonElement Config);

    public sealed record Response(Guid Id, Guid VersionId, int Version);
}
