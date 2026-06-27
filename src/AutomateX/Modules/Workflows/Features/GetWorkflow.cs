using System.Text.Json;
using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Modules.Triggers;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Workflows.Features;

public static class GetWorkflow
{
    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access) : EndpointWithoutRequest<Response>
    {
        public override void Configure()
        {
            Get("workflows/{id}");
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
                .Where(x => x.Id == id && x.WorkspaceId == ws)
                .Select(x => new
                {
                    x.Id,
                    x.Name,
                    x.Description,
                    x.CreatedAt,
                    x.Enabled,
                    LatestVersion = x.Versions
                        .OrderByDescending(v => v.Version)
                        .Select(v => new VersionResponse(
                            v.Id,
                            v.Version,
                            v.CreatedAt,
                            v.ContinueOnFailure,
                            v.Steps
                                .OrderBy(s => s.Order)
                                .Select(s => new StepResponse(s.Id, s.Order, s.Key, s.Name, s.ActionType, s.ConfigJson, s.IdempotencyKey))
                                .ToList(),
                            v.Edges
                                .Select(e => new EdgeResponse(e.FromOrder, e.ToOrder, e.Label))
                                .ToList()))
                        .First(),
                    Versions = x.Versions
                        .OrderByDescending(v => v.Version)
                        .Select(v => new VersionSummary(v.Id, v.Version, v.CreatedAt, v.Steps.Count))
                        .ToList(),
                })
                .FirstOrDefaultAsync(ct);

            if (workflow is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            var triggers = await dbContext.Triggers
                .AsNoTracking()
                .Where(x => x.WorkflowId == id)
                .Select(x => new TriggerResponse(
                    x.Id, x.Type, x.Enabled, x.EntryStepOrder, x.NextRunAt, x.LastFiredAt, x.LastError, x.LastErrorAt, x.ConfigJson))
                .ToListAsync(ct);

            var (runsAfter, feeds) = await ChainLinksAsync(id, ws, ct);

            await Send.OkAsync(
                new Response(
                    workflow.Id, workflow.Name, workflow.Description, workflow.CreatedAt, workflow.Enabled,
                    workflow.LatestVersion, workflow.Versions, triggers, runsAfter, feeds),
                ct);
        }

        // Both directions of the chain graph around this workflow:
        // runsAfter = workflows whose completion triggers this one (own workflow-triggers);
        // feeds = workflows holding a workflow-trigger that watches this one.
        private async Task<(List<ChainLink> RunsAfter, List<ChainLink> Feeds)> ChainLinksAsync(
            Guid id, Guid ws, CancellationToken ct)
        {
            var chainTriggers = await dbContext.Triggers
                .AsNoTracking()
                .Where(x => x.Enabled && x.Type == TriggerTypes.Workflow)
                .Select(x => new { x.WorkflowId, x.ConfigJson })
                .ToListAsync(ct);

            List<(Guid WorkflowId, string On)> runsAfter = [];
            List<(Guid WorkflowId, string On)> feeds = [];
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

                if (config is null)
                {
                    continue;
                }

                if (trigger.WorkflowId == id)
                {
                    runsAfter.Add((config.WorkflowId, config.On));
                }

                if (config.WorkflowId == id)
                {
                    feeds.Add((trigger.WorkflowId, config.On));
                }
            }

            var ids = runsAfter.Concat(feeds).Select(x => x.WorkflowId).Distinct().ToList();
            var names = await dbContext.Workflows
                .AsNoTracking()
                .Where(x => ids.Contains(x.Id) && x.WorkspaceId == ws)
                .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

            List<ChainLink> Resolve(List<(Guid WorkflowId, string On)> links) => links
                .Where(x => names.ContainsKey(x.WorkflowId))
                .Select(x => new ChainLink(x.WorkflowId, names[x.WorkflowId], x.On))
                .ToList();

            return (Resolve(runsAfter), Resolve(feeds));
        }
    }

    public sealed record Response(
        Guid Id,
        string Name,
        string? Description,
        DateTimeOffset CreatedAt,
        bool Enabled,
        VersionResponse LatestVersion,
        List<VersionSummary> Versions,
        List<TriggerResponse> Triggers,
        List<ChainLink> RunsAfter,
        List<ChainLink> Feeds);

    public sealed record ChainLink(Guid WorkflowId, string Name, string On);

    public sealed record VersionResponse(
        Guid Id, int Version, DateTimeOffset CreatedAt, bool ContinueOnFailure, List<StepResponse> Steps, List<EdgeResponse> Edges);

    public sealed record VersionSummary(Guid Id, int Version, DateTimeOffset CreatedAt, int StepCount);

    public sealed record StepResponse(
        Guid Id, int Order, string Key, string? Name, string ActionType, string ConfigJson, string? IdempotencyKey);

    public sealed record EdgeResponse(int From, int To, string? Label);

    public sealed record TriggerResponse(
        Guid Id, string Type, bool Enabled, int? EntryStepOrder, DateTimeOffset? NextRunAt, DateTimeOffset? LastFiredAt,
        string? LastError, DateTimeOffset? LastErrorAt, string ConfigJson);
}
