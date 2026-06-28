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
        WorkspaceAccess access,
        Audit.IAuditSink audit) : Endpoint<Request, Response>
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
                    case TriggerTypes.OnFailure:
                        await ValidateOnFailureConfigOrThrowAsync(configJson, ws, ct);
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

            // Tri-state: absent = leave unchanged; JSON null = reset to the default (first step);
            // a number = set that entry step (validated against the latest version).
            if (req.EntryStepOrder is { } entryElement)
            {
                if (entryElement.ValueKind is not (JsonValueKind.Null or JsonValueKind.Number))
                {
                    ThrowError("entryStepOrder must be a step index or null.");
                }

                int? newEntry = entryElement.ValueKind == JsonValueKind.Number ? entryElement.GetInt32() : null;
                if (newEntry is { } e)
                {
                    await TriggerEntry.ValidateOrThrowAsync(dbContext, trigger.WorkflowId, e, m => ThrowError(m), ct);
                }

                trigger.SetEntryStep(newEntry);
            }

            await dbContext.SaveChangesAsync(ct);
            await audit.RecordAsync(
                "trigger.update", ws, WorkspaceAccess.GetActor(User),
                "trigger", trigger.Id.ToString(), trigger.Type, ct);
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

        private async Task ValidateOnFailureConfigOrThrowAsync(string configJson, Guid workspaceId, CancellationToken ct)
        {
            FailureAlerting.OnFailureConfig? config;
            try
            {
                config = JsonSerializer.Deserialize<FailureAlerting.OnFailureConfig>(configJson, JsonSerializerOptions.Web);
            }
            catch (JsonException)
            {
                ThrowError("Invalid execution.onFailure config — expected { watchWorkflowId?, includeSubWorkflows? }.");
                return;
            }

            if (config?.WatchWorkflowId is { } watched
                && !await dbContext.Workflows.AnyAsync(x => x.Id == watched && x.WorkspaceId == workspaceId, ct))
            {
                ThrowError("watchWorkflowId was not found in this workspace.");
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

    public sealed record Request(JsonElement? Config, bool? Enabled, JsonElement? EntryStepOrder = null);

    public sealed record Response(Guid Id, string Type, bool Enabled, DateTimeOffset? NextRunAt);
}
