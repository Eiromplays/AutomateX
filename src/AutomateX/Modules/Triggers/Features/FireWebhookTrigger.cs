using AutomateX.Database;
using AutomateX.Engine;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace AutomateX.Modules.Triggers.Features;

public static class FireWebhookTrigger
{
    public sealed class Endpoint(AutomateXDbContext dbContext, IMessageBus bus) : EndpointWithoutRequest<Response>
    {
        public override void Configure()
        {
            Post("webhooks/{triggerId}");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            var triggerId = Route<Guid>("triggerId");

            var trigger = await dbContext.Triggers
                .FirstOrDefaultAsync(x => x.Id == triggerId && x.Type == TriggerTypes.Webhook, ct);

            if (trigger is null || !trigger.Enabled)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            var executionId = Guid.CreateVersion7();
            await bus.PublishAsync(new RunWorkflow(executionId, trigger.WorkflowId, $"webhook:{trigger.Id}"));

            trigger.MarkFired(nextRunAt: null);
            await dbContext.SaveChangesAsync(ct);

            await Send.OkAsync(new Response(executionId), ct);
        }
    }

    public sealed record Response(Guid ExecutionId);
}
