using System.Text.Json;
using AutomateX.Database;
using Cronos;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Triggers.Features;

public static class CreateTrigger
{
    public sealed class Endpoint(AutomateXDbContext dbContext) : Endpoint<Request, Response>
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

            switch (req.Type)
            {
                case TriggerTypes.Cron:
                    nextRunAt = ParseCronOrThrow(configJson);
                    break;
                case TriggerTypes.Webhook:
                    break;
                default:
                    ThrowError($"Unknown trigger type '{req.Type}'. Supported: {TriggerTypes.Cron}, {TriggerTypes.Webhook}.");
                    return;
            }

            var trigger = Trigger.Create(req.WorkflowId, req.Type, configJson, nextRunAt);
            dbContext.Triggers.Add(trigger);
            await dbContext.SaveChangesAsync(ct);

            await Send.OkAsync(new Response(trigger.Id, trigger.Type, trigger.Enabled, trigger.NextRunAt), ct);
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

    public sealed record Response(Guid Id, string Type, bool Enabled, DateTimeOffset? NextRunAt);
}
