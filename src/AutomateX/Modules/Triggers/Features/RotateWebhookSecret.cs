using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutomateX.Modules.Triggers.Features;

// Replaces a webhook trigger's secret without recreating the trigger — the old URL
// stops working immediately; the new one is shown exactly once.
public static class RotateWebhookSecret
{
    public sealed class Endpoint(AutomateXDbContext dbContext, IOptions<EngineOptions> engineOptions, WorkspaceAccess access) : EndpointWithoutRequest<Response>
    {
        public override void Configure()
        {
            Post("triggers/{id}/rotate-secret");
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

            var trigger = await dbContext.Triggers
                .FirstOrDefaultAsync(x => x.Id == id && x.Type == TriggerTypes.Webhook
                    && dbContext.Workflows.Any(w => w.Id == x.WorkflowId && w.WorkspaceId == ws), ct);

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
                WebhookSecret.BuildUrl(engineOptions.Value.PublicBaseUrl, trigger.Id)), ct);
        }
    }

    public sealed record Response(Guid Id, string WebhookSecret, string WebhookUrl);
}
