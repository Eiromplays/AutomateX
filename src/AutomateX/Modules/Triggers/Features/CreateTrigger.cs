using System.Text.Json;
using AutomateX.Database;
using AutomateX.Engine;
using Cronos;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutomateX.Modules.Triggers.Features;

public static class CreateTrigger
{
    public sealed class Endpoint(AutomateXDbContext dbContext, IOptions<EngineOptions> engineOptions) : Endpoint<Request, Response>
    {
        public override void Configure()
        {
            Post("workflows/{workflowId}/triggers");
            AllowAnonymous();
        }

        public override async Task HandleAsync(Request req, CancellationToken ct)
        {
            if (!await dbContext.Workflows.AnyAsync(x => x.Id == req.WorkflowId, ct))
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            var configJson = req.Config is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } config
                ? config.GetRawText()
                : "{}";

            DateTimeOffset? nextRunAt = null;
            string? webhookSecret = null;

            switch (req.Type)
            {
                case TriggerTypes.Cron:
                    nextRunAt = ParseCronOrThrow(configJson);
                    break;
                case TriggerTypes.Webhook:
                    (configJson, webhookSecret) = WebhookSecret.AddTo(configJson);
                    break;
                default:
                    ThrowError($"Unknown trigger type '{req.Type}'. Supported: {TriggerTypes.Cron}, {TriggerTypes.Webhook}.");
                    return;
            }

            var trigger = Trigger.Create(req.WorkflowId, req.Type, configJson, nextRunAt);
            dbContext.Triggers.Add(trigger);
            await dbContext.SaveChangesAsync(ct);

            // The webhook secret is shown exactly once — it is not retrievable afterwards.
            await Send.OkAsync(new Response(
                trigger.Id,
                trigger.Type,
                trigger.Enabled,
                trigger.NextRunAt,
                webhookSecret,
                webhookSecret is null
                    ? null
                    : WebhookSecret.BuildUrl(engineOptions.Value.PublicBaseUrl, trigger.Id, webhookSecret)), ct);
        }

        private DateTimeOffset? ParseCronOrThrow(string configJson)
        {
            CronTriggerConfig? config = null;
            try
            {
                config = JsonSerializer.Deserialize<CronTriggerConfig>(configJson, JsonSerializerOptions.Web);
            }
            catch (JsonException)
            {
                // handled below
            }

            if (string.IsNullOrWhiteSpace(config?.Cron))
            {
                ThrowError("""Cron triggers require config: { "cron": "<expression>" }.""");
            }

            try
            {
                return CronExpression.Parse(config.Cron).GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Utc);
            }
            catch (CronFormatException)
            {
                ThrowError($"Invalid cron expression '{config.Cron}'.");
                return null;
            }
        }
    }

    public sealed record Request(Guid WorkflowId, string Type, JsonElement? Config);

    public sealed record Response(
        Guid Id,
        string Type,
        bool Enabled,
        DateTimeOffset? NextRunAt,
        string? WebhookSecret,
        string? WebhookUrl);
}
