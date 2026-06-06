using System.Text.Json;
using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Web;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace AutomateX.Modules.Workflows.Features;

public static class ExecuteWorkflow
{
    public sealed class Endpoint(AutomateXDbContext dbContext, IMessageBus bus) : EndpointWithoutRequest<Response>
    {
        public override void Configure()
        {
            Post("workflows/{id}/execute");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            var id = Route<Guid>("id");

            if (!await dbContext.Workflows.AnyAsync(x => x.Id == id, ct))
            {
                await Send.NotFoundAsync(ct);
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
