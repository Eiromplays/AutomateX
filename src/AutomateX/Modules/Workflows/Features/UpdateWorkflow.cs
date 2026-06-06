using System.Text.Json;
using AutomateX.Database;
using AutomateX.Engine.Actions;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Workflows.Features;

public static class UpdateWorkflow
{
    public sealed class Endpoint(AutomateXDbContext dbContext, ActionRegistry actions) : Endpoint<Request, Response>
    {
        public override void Configure()
        {
            Put("workflows/{id}");
            AllowAnonymous();
        }

        public override async Task HandleAsync(Request req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req.Name))
            {
                ThrowError("Name is required.");
            }

            foreach (var step in req.Steps.Where(step => !actions.Contains(step.ActionType)))
            {
                ThrowError($"Unknown action type '{step.ActionType}'.");
            }

            var workflow = await dbContext.Workflows
                .Include(x => x.Versions)
                .FirstOrDefaultAsync(x => x.Id == req.Id, ct);

            if (workflow is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            workflow.Rename(req.Name, req.Description);
            var version = workflow.AddVersion(req.Steps
                .Select(x => new StepDefinition(x.ActionType, x.Name, x.Config.GetRawText()))
                .ToList());

            // Explicit Add — see ExecuteStepHandler: discovered children with client-set keys track as Modified.
            dbContext.WorkflowVersions.Add(version);

            await dbContext.SaveChangesAsync(ct);

            await Send.OkAsync(new Response(workflow.Id, version.Id, version.Version), ct);
        }
    }

    public sealed record Request(Guid Id, string Name, string? Description, List<CreateWorkflow.StepRequest> Steps);

    public sealed record Response(Guid Id, Guid VersionId, int Version);
}
