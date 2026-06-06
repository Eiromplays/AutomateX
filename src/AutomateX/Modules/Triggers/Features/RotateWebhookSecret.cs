using AutomateX.Database;
using AutomateX.Engine;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutomateX.Modules.Triggers.Features;

// Replaces a webhook trigger's secret without recreating the trigger — the old URL
// stops working immediately; the new one is shown exactly once.
public static class RotateWebhookSecret
{
    public sealed class Endpoint(AutomateXDbContext dbContext, IOptions<EngineOptions> engineOptions) : EndpointWithoutRequest<Response>
    {
        public override void Configure()
        {
            Post("triggers/{id}/rotate-secret");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            var id = Route<Guid>("id");

            var trigger = await dbContext.Triggers
                .FirstOrDefaultAsync(x => x.Id == id && x.Type == TriggerTypes.Webhook, ct);

            if (trigger is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            var (configJson, secret) = WebhookSecret.AddTo(trigger.ConfigJson);
            trigger.ReplaceConfig(configJson);
            await dbContext.SaveChangesAsync(ct);

            await Send.OkAsync(new Response(
                trigger.Id,
                secret,
                WebhookSecret.BuildUrl(engineOptions.Value.PublicBaseUrl, trigger.Id, secret)), ct);
        }
    }

    public sealed record Response(Guid Id, string WebhookSecret, string WebhookUrl);
}
