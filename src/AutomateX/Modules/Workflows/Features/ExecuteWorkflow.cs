using AutomateX.Database;
using AutomateX.Engine;
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

            var executionId = Guid.CreateVersion7();
            await bus.PublishAsync(new RunWorkflow(executionId, id, "manual"));

            await Send.OkAsync(new Response(executionId), ct);
        }
    }

    public sealed record Response(Guid ExecutionId);
}
