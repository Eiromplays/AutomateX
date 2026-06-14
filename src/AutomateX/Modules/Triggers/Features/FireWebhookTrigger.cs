using System.Text.Json;
using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Web;
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

            // Per-trigger secret (header or ?secret=) — webhooks sit outside the global
            // API-key gate. Legacy triggers without a stored secret must be recreated.
            var providedSecret = HttpContext.Request.Headers["X-Webhook-Secret"].FirstOrDefault()
                ?? HttpContext.Request.Query["secret"].FirstOrDefault();
            if (!WebhookSecret.Validate(trigger.ConfigJson, providedSecret))
            {
                await Send.UnauthorizedAsync(ct);
                return;
            }

            string? payload = null;
            try
            {
                payload = await RawJsonBody.ReadAsync(HttpContext, ct);
            }
            catch (JsonException)
            {
                ThrowError("Webhook body must be empty or valid JSON — it becomes {{trigger.payload}}.");
            }

            var executionId = Guid.CreateVersion7();
            await bus.PublishAsync(new RunWorkflow(executionId, trigger.WorkflowId, $"webhook:{trigger.Id}", payload, trigger.EntryStepOrder));

            trigger.MarkFired(nextRunAt: null);
            await dbContext.SaveChangesAsync(ct);

            await Send.OkAsync(new Response(executionId), ct);
        }
    }

    public sealed record Response(Guid ExecutionId);
}
