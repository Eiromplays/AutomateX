using System.Text.Json;
using System.Text.Json.Nodes;
using AutomateX.Database;
using AutomateX.Engine.Actions;
using AutomateX.Modules.Triggers;
using AutomateX.Modules.Workspaces;
using Cronos;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Workflows.Features;

public static class ExportWorkflow
{
    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access) : EndpointWithoutRequest<JsonObject>
    {
        public override void Configure()
        {
            Get("workflows/{id}/export");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Viewer, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var id = Route<Guid>("id");

            var workflow = await dbContext.Workflows
                .AsNoTracking()
                .Include(x => x.Versions)
                .ThenInclude(x => x.Steps)
                .Include(x => x.Versions)
                .ThenInclude(x => x.Edges)
                .FirstOrDefaultAsync(x => x.Id == id && x.WorkspaceId == ws, ct);

            if (workflow is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            var latest = workflow.Versions.OrderByDescending(x => x.Version).First();
            var steps = latest.Steps
                .OrderBy(x => x.Order)
                .Select(x => new StepDefinition(x.ActionType, x.Name, x.ConfigJson))
                .ToList();
            var edges = latest.Edges
                .Select(x => new EdgeDefinition(x.FromOrder, x.ToOrder, x.Label))
                .ToList();

            var triggers = await dbContext.Triggers
                .AsNoTracking()
                .Where(x => x.WorkflowId == id)
                .Select(x => new { x.Type, x.ConfigJson })
                .ToListAsync(ct);

            await Send.OkAsync(
                WorkflowTransfer.Export(
                    workflow.Name,
                    workflow.Description,
                    steps,
                    triggers.Select(x => (x.Type, x.ConfigJson)).ToList(),
                    edges),
                ct);
        }
    }
}

public static class ImportWorkflow
{
    public sealed class Endpoint(
        AutomateXDbContext dbContext,
        ActionRegistry actions,
        WorkspaceAccess access) : Endpoint<JsonObject, Response>
    {
        public override void Configure()
        {
            Post("workflows/import");
            AllowAnonymous();
        }

        public override async Task HandleAsync(JsonObject req, CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Editor, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            WorkflowTransfer.ParsedImport parsed;
            try
            {
                parsed = WorkflowTransfer.Parse(req);
            }
            catch (InvalidOperationException exception)
            {
                ThrowError(exception.Message);
                return;
            }

            var unknown = parsed.Steps
                .Select(x => x.ActionType)
                .Distinct()
                .Where(type => !actions.Contains(type, ws))
                .ToList();

            if (unknown.Count > 0)
            {
                ThrowError($"Unknown action types: {string.Join(", ", unknown)}. "
                    + "Install the plugins providing them, then import again.");
            }

            var workflow = Workflow.Create(parsed.Name, parsed.Description, ws);
            var version = workflow.AddVersion(parsed.Steps.ToList(), parsed.Edges.ToList());
            dbContext.Workflows.Add(workflow);

            foreach (var configJson in parsed.CronTriggerConfigs)
            {
                dbContext.Triggers.Add(Trigger.Create(
                    workflow.Id, TriggerTypes.Cron, configJson, NextCronOccurrenceOrThrow(configJson)));
            }

            await dbContext.SaveChangesAsync(ct);
            await Send.OkAsync(new Response(workflow.Id, version.Id, version.Version), ct);
        }

        private DateTimeOffset? NextCronOccurrenceOrThrow(string configJson)
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
                ThrowError("An imported cron trigger has no cron expression.");
            }

            try
            {
                return CronExpression.Parse(config.Cron).GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Utc);
            }
            catch (CronFormatException)
            {
                ThrowError($"Invalid cron expression '{config.Cron}' in imported trigger.");
                return null;
            }
        }
    }

    public sealed record Response(Guid Id, Guid VersionId, int Version);
}
