using AutomateX.Database;
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
                    LatestVersion = x.Versions
                        .OrderByDescending(v => v.Version)
                        .Select(v => new VersionResponse(
                            v.Id,
                            v.Version,
                            v.CreatedAt,
                            v.Steps
                                .OrderBy(s => s.Order)
                                .Select(s => new StepResponse(s.Id, s.Order, s.Name, s.ActionType, s.ConfigJson))
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
                .Select(x => new TriggerResponse(x.Id, x.Type, x.Enabled, x.NextRunAt, x.LastFiredAt))
                .ToListAsync(ct);

            await Send.OkAsync(
                new Response(workflow.Id, workflow.Name, workflow.Description, workflow.CreatedAt, workflow.LatestVersion, workflow.Versions, triggers),
                ct);
        }
    }

    public sealed record Response(
        Guid Id,
        string Name,
        string? Description,
        DateTimeOffset CreatedAt,
        VersionResponse LatestVersion,
        List<VersionSummary> Versions,
        List<TriggerResponse> Triggers);

    public sealed record VersionResponse(Guid Id, int Version, DateTimeOffset CreatedAt, List<StepResponse> Steps);

    public sealed record VersionSummary(Guid Id, int Version, DateTimeOffset CreatedAt, int StepCount);

    public sealed record StepResponse(Guid Id, int Order, string? Name, string ActionType, string ConfigJson);

    public sealed record TriggerResponse(Guid Id, string Type, bool Enabled, DateTimeOffset? NextRunAt, DateTimeOffset? LastFiredAt);
}
