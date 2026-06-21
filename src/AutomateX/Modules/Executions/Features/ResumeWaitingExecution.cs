using System.Text.Json;
using AutomateX.Database;
using AutomateX.Modules.Workspaces;
using AutomateX.Web;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace AutomateX.Modules.Executions.Features;

// Resume a suspended (Waiting) execution — the UI "approve/resume" path. The optional JSON body
// becomes the wait step's output, so a downstream gate/switch can branch on the decision.
public static class ResumeWaitingExecution
{
    public sealed class Endpoint(
        AutomateXDbContext dbContext,
        IMessageBus bus,
        WorkspaceAccess access) : EndpointWithoutRequest<Response>
    {
        public override void Configure()
        {
            Post("executions/{id}/resume");
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
            var execution = await dbContext.Executions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id && x.WorkspaceId == ws, ct);

            if (execution is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            if (execution.Status != ExecutionStatus.Waiting)
            {
                ThrowError("This execution is not waiting for a resume.");
            }

            var stepOrder = await dbContext.StepExecutions
                .Where(x => x.ExecutionId == id && x.Status == ExecutionStatus.Waiting)
                .Select(x => (int?)x.StepOrder)
                .FirstOrDefaultAsync(ct);

            if (stepOrder is null)
            {
                ThrowError("No waiting step to resume.");
                return;
            }

            string? payload = null;
            try
            {
                payload = await RawJsonBody.ReadAsync(HttpContext, ct);
            }
            catch (JsonException)
            {
                ThrowError("Request body must be empty or valid JSON — it becomes the wait step's output.");
            }

            await bus.PublishAsync(new Engine.ResumeExecution(id, stepOrder.Value, "resumed", payload));
            await Send.OkAsync(new Response(id, stepOrder.Value), ct);
        }
    }

    public sealed record Response(Guid ExecutionId, int StepOrder);
}
