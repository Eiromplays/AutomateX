using System.Text.Json;
using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Modules.Triggers;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Workflows.Features;

public static class GetWorkflows
{
    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access) : EndpointWithoutRequest<List<Response>>
    {
        public override void Configure()
        {
            Get("workflows");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Viewer, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var workflows = await dbContext.Workflows
                .AsNoTracking()
                .Where(x => x.WorkspaceId == ws)
                .OrderBy(x => x.Name)
                .Select(x => new
                {
                    x.Id,
                    x.Name,
                    x.Description,
                    x.CreatedAt,
                    x.Enabled,
                    LatestVersion = x.Versions.Max(v => v.Version),
                })
                .ToListAsync(ct);

            // Chain relationships: trigger on B watching A means B runs after A / A feeds B.
            var chainTriggers = await dbContext.Triggers
                .AsNoTracking()
                .Where(x => x.Enabled && x.Type == TriggerTypes.Workflow)
                .Select(x => new { x.WorkflowId, x.ConfigJson })
                .ToListAsync(ct);

            var names = workflows.ToDictionary(x => x.Id, x => x.Name);
            Dictionary<Guid, List<string>> runsAfter = [];
            Dictionary<Guid, List<string>> feeds = [];
            foreach (var trigger in chainTriggers)
            {
                WorkflowChaining.ChainConfig? config;
                try
                {
                    config = JsonSerializer.Deserialize<WorkflowChaining.ChainConfig>(trigger.ConfigJson, JsonSerializerOptions.Web);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (config is null
                    || !names.TryGetValue(trigger.WorkflowId, out var targetName)
                    || !names.TryGetValue(config.WorkflowId, out var watchedName))
                {
                    continue;
                }

                (runsAfter.TryGetValue(trigger.WorkflowId, out var after) ? after : runsAfter[trigger.WorkflowId] = []).Add(watchedName);
                (feeds.TryGetValue(config.WorkflowId, out var into) ? into : feeds[config.WorkflowId] = []).Add(targetName);
            }

            await Send.OkAsync(workflows
                .Select(x => new Response(
                    x.Id,
                    x.Name,
                    x.Description,
                    x.CreatedAt,
                    x.Enabled,
                    x.LatestVersion,
                    runsAfter.TryGetValue(x.Id, out var ra) ? ra : [],
                    feeds.TryGetValue(x.Id, out var fi) ? fi : []))
                .ToList(), ct);
        }
    }

    public sealed record Response(
        Guid Id,
        string Name,
        string? Description,
        DateTimeOffset CreatedAt,
        bool Enabled,
        int LatestVersion,
        List<string> RunsAfter,
        List<string> Feeds);
}
