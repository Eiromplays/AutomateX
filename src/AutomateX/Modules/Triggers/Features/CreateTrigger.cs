using System.Text.Json;
using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Modules.Workspaces;
using Cronos;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutomateX.Modules.Triggers.Features;

public static class CreateTrigger
{
    public sealed class Endpoint(
        AutomateXDbContext dbContext,
        IOptions<EngineOptions> engineOptions,
        AutomateX.Engine.Triggers.TriggerRegistry triggerRegistry,
        WorkspaceAccess access) : Endpoint<Request, Response>
    {
        public override void Configure()
        {
            Post("workflows/{workflowId}/triggers");
            AllowAnonymous();
        }

        public override async Task HandleAsync(Request req, CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Editor, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            if (!await dbContext.Workflows.AnyAsync(x => x.Id == req.WorkflowId && x.WorkspaceId == ws, ct))
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            if (req.EntryStepOrder is { } entry)
            {
                await TriggerEntry.ValidateOrThrowAsync(dbContext, req.WorkflowId, entry, m => ThrowError(m), ct);
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
                case TriggerTypes.Workflow:
                    await ValidateChainConfigOrThrowAsync(configJson, ws, ct);
                    break;
                default:
                    if (!triggerRegistry.Contains(req.Type))
                    {
                        ThrowError($"Unknown trigger type '{req.Type}'. Supported: {TriggerTypes.Cron}, "
                            + $"{TriggerTypes.Webhook}, {TriggerTypes.Workflow}"
                            + (triggerRegistry.Types.Count > 0 ? $", {string.Join(", ", triggerRegistry.Types)}." : "."));
                        return;
                    }

                    break; // plugin trigger: config is the listener's business; the host validates at start
            }

            var trigger = Trigger.Create(req.WorkflowId, req.Type, configJson, nextRunAt, req.EntryStepOrder);
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

            // The watched workflow must exist in the caller's workspace - chains never cross workspaces.
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

    public sealed record Request(Guid WorkflowId, string Type, JsonElement? Config, int? EntryStepOrder = null);

    public sealed record Response(
        Guid Id,
        string Type,
        bool Enabled,
        DateTimeOffset? NextRunAt,
        string? WebhookSecret,
        string? WebhookUrl);
}
