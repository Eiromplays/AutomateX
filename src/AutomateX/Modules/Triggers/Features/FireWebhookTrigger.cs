using System.Text.Json;
using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Web;
using FastEndpoints;
using Microsoft.AspNetCore.RateLimiting;
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
            Options(b => b.RequireRateLimiting(RateLimitPolicies.Webhook));
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

            // Read the raw body first: the HMAC signature is computed over exactly these bytes.
            var rawBody = await RawJsonBody.ReadRawAsync(HttpContext, ct);

            // Per-trigger secret — webhooks sit outside the global API-key gate. Preferred:
            // X-Webhook-Signature (HMAC-SHA256 of the body); X-Webhook-Secret (plaintext) also
            // accepted. Legacy triggers without a stored secret must be recreated.
            var signature = HttpContext.Request.Headers[WebhookSecret.SignatureHeader].FirstOrDefault();
            var plaintextSecret = HttpContext.Request.Headers[WebhookSecret.SecretHeader].FirstOrDefault();
            if (!WebhookSecret.Validate(trigger.ConfigJson, rawBody, signature, plaintextSecret))
            {
                await Send.UnauthorizedAsync(ct);
                return;
            }

            string? payload = null;
            if (!string.IsNullOrWhiteSpace(rawBody))
            {
                try
                {
                    using var _ = JsonDocument.Parse(rawBody);
                }
                catch (JsonException)
                {
                    ThrowError("Webhook body must be empty or valid JSON — it becomes {{trigger.payload}}.");
                }

                payload = rawBody;
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
