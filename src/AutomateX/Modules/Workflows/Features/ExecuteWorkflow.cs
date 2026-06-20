using System.Text.Json;
using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Modules.Workspaces;
using AutomateX.Web;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace AutomateX.Modules.Workflows.Features;

public static class ExecuteWorkflow
{
    public sealed class Endpoint(AutomateXDbContext dbContext, IMessageBus bus, WorkspaceAccess access) : EndpointWithoutRequest<Response>
    {
        public override void Configure()
        {
            Post("workflows/{id}/execute");
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

            var workflow = await dbContext.Workflows
                .Where(x => x.Id == id && x.WorkspaceId == ws)
                .Select(x => new { x.Enabled })
                .FirstOrDefaultAsync(ct);

            if (workflow is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            // The engine drops disabled runs silently; surface a clear error for the manual path.
            if (!workflow.Enabled)
            {
                ThrowError("This workflow is disabled — enable it before running.");
                return;
            }

            string? payload = null;
            try
            {
                payload = await RawJsonBody.ReadAsync(HttpContext, ct);
            }
            catch (JsonException)
            {
                ThrowError("Request body must be empty or valid JSON — it becomes {{trigger.payload}}.");
            }

            var executionId = Guid.CreateVersion7();
            await bus.PublishAsync(new RunWorkflow(executionId, id, "manual", payload));

            await Send.OkAsync(new Response(executionId), ct);
        }
    }

    public sealed record Response(Guid ExecutionId);
}
