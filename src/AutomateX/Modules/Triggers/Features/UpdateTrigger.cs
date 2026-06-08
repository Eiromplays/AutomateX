using System.Text.Json;
using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Engine.Triggers;
using AutomateX.Modules.Workspaces;
using Cronos;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Triggers.Features;

public static class UpdateTrigger
{
    public sealed class Endpoint(
        AutomateXDbContext dbContext,
        TriggerRegistry triggerRegistry,
        WorkspaceAccess access) : Endpoint<Request, Response>
    {
        public override void Configure()
        {
            Put("triggers/{id}");
            AllowAnonymous();
        }

        public override async Task HandleAsync(Request req, CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Editor, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var id = Route<Guid>("id");
            var trigger = await dbContext.Triggers
                .FirstOrDefaultAsync(
                    x => x.Id == id && dbContext.Workflows.Any(w => w.Id == x.WorkflowId && w.WorkspaceId == ws),
                    ct);

            if (trigger is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            if (req.Enabled is { } enabled)
            {
                trigger.SetEnabled(enabled);
            }

            if (req.Config is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } config)
            {
                if (!TriggerEditRules.CanEditConfig(trigger.Type))
                {
                    ThrowError("Webhook config can't be edited — rotate its secret instead.");
                }

                var configJson = config.GetRawText();
                DateTimeOffset? nextRunAt = null;

                switch (trigger.Type)
                {
                    case TriggerTypes.Cron:
                        nextRunAt = ParseCronOrThrow(configJson);
                        break;
                    case TriggerTypes.Workflow:
                        await ValidateChainConfigOrThrowAsync(configJson, ws, ct);
                        break;
                    default:
                        if (!triggerRegistry.Contains(trigger.Type))
                        {
                            ThrowError($"Unknown trigger type '{trigger.Type}'.");
                        }

                        break; // plugin trigger: the host re-validates and restarts on the config change
                }

                trigger.Reconfigure(configJson, nextRunAt);
            }

            await dbContext.SaveChangesAsync(ct);
            await Send.OkAsync(new Response(trigger.Id, trigger.Type, trigger.Enabled, trigger.NextRunAt), ct);
        }

        private async Task ValidateChainConfigOrThrowAsync(string configJson, Guid workspaceId, CancellationToken ct)
        {
            WorkflowChaining.ChainConfig? config = null;
            try
            {
                config = JsonSerializer.Deserialize<WorkflowChaining.ChainConfig>(configJson, JsonSerializerOptions.Web);
            }
            catch (JsonException)
            {
            }

            if (config is null || config.WorkflowId == Guid.Empty)
            {
                ThrowError("Workflow triggers require config { workflowId, on } with on = succeeded | failed | any.");
                return;
            }

            if (config.On is not ("succeeded" or "failed" or "any"))
            {
                ThrowError("Workflow trigger 'on' must be succeeded, failed or any.");
            }

            if (!await dbContext.Workflows.AnyAsync(x => x.Id == config.WorkflowId && x.WorkspaceId == workspaceId, ct))
            {
                ThrowError("The watched workflow was not found in this workspace.");
            }
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

    public sealed record Request(JsonElement? Config, bool? Enabled);

    public sealed record Response(Guid Id, string Type, bool Enabled, DateTimeOffset? NextRunAt);
}
