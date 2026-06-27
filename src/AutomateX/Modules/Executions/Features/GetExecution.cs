using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Executions.Features;

public static class GetExecution
{
    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access) : EndpointWithoutRequest<Response>
    {
        public override void Configure()
        {
            Get("executions/{id}");
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

            var execution = await dbContext.Executions
                .AsNoTracking()
                .Where(x => x.Id == id && x.WorkspaceId == ws)
                .Select(x => new Response(
                    x.Id,
                    x.WorkflowId,
                    x.WorkflowVersionId,
                    x.TriggeredBy,
                    x.TriggerPayload,
                    x.Status.ToString(),
                    x.StartedAt,
                    x.CompletedAt,
                    x.Steps
                        .OrderBy(s => s.StepOrder)
                        .Select(s => new StepResponse(
                            s.Id,
                            s.StepOrder,
                            s.ActionType,
                            s.Status.ToString(),
                            s.Attempts,
                            s.Status == ExecutionStatus.Succeeded ? s.Attempts - 1 : s.Attempts,
                            s.Output,
                            s.Error,
                            s.StartedAt,
                            s.CompletedAt))
                        .ToList(),
                    x.ParentExecutionId))
                .FirstOrDefaultAsync(ct);

            if (execution is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            // The exact version that ran (not "latest") — so the inspector graph shows the real
            // lanes, with every step as a node even if it was skipped or never reached.
            var version = await dbContext.WorkflowVersions
                .AsNoTracking()
                .Where(v => v.Id == execution.WorkflowVersionId)
                .Select(v => new
                {
                    v.Version,
                    Steps = v.Steps.OrderBy(s => s.Order)
                        .Select(s => new VersionStepResponse(s.Order, s.Name, s.ActionType)).ToList(),
                    Edges = v.Edges.Select(e => new EdgeResponse(e.FromOrder, e.ToOrder, e.Label)).ToList(),
                })
                .FirstOrDefaultAsync(ct);

            // Downstream lineage: executions this one chained into. Workflow-triggered
            // executions carry source.executionId in their payload; hobby-scale scan.
            var candidates = await dbContext.Executions
                .AsNoTracking()
                .Where(x => x.WorkspaceId == ws && x.TriggeredBy == WorkflowChaining.TriggerType)
                .OrderByDescending(x => x.StartedAt)
                .Take(200)
                .Select(x => new { x.Id, x.WorkflowId, Status = x.Status.ToString(), x.TriggerPayload })
                .ToListAsync(ct);

            var chained = candidates
                .Where(x => WorkflowChaining.GetSourceExecutionId(x.TriggerPayload) == id)
                .Select(x => new ChainedResponse(x.Id, x.WorkflowId, x.Status))
                .ToList();

            // Sub-workflow children: runs this execution called via workflow.call.
            var children = await dbContext.Executions
                .AsNoTracking()
                .Where(x => x.ParentExecutionId == id)
                .OrderBy(x => x.StartedAt)
                .Select(x => new ChainedResponse(x.Id, x.WorkflowId, x.Status.ToString()))
                .ToListAsync(ct);
            chained.AddRange(children);

            // Downstream retries: reruns carry TriggeredBy "retry:<thisId>", so the
            // original execution can link forward to every replay of it.
            var retryTag = $"retry:{id}";
            var retries = await dbContext.Executions
                .AsNoTracking()
                .Where(x => x.WorkspaceId == ws && x.TriggeredBy == retryTag)
                .OrderByDescending(x => x.StartedAt)
                .Select(x => new ChainedResponse(x.Id, x.WorkflowId, x.Status.ToString()))
                .ToListAsync(ct);

            await Send.OkAsync(
                execution with
                {
                    Chained = chained,
                    Retries = retries,
                    WorkflowVersion = version?.Version,
                    WorkflowSteps = version?.Steps ?? [],
                    Edges = version?.Edges ?? [],
                },
                ct);
        }
    }

    public sealed record Response(
        Guid Id,
        Guid WorkflowId,
        Guid WorkflowVersionId,
        string TriggeredBy,
        string? TriggerPayload,
        string Status,
        DateTimeOffset StartedAt,
        DateTimeOffset? CompletedAt,
        List<StepResponse> Steps,
        Guid? ParentExecutionId = null)
    {
        public List<ChainedResponse> Chained { get; init; } = [];

        public List<ChainedResponse> Retries { get; init; } = [];

        // The ran version's number and full topology, for the inspector.
        public int? WorkflowVersion { get; init; }

        public List<VersionStepResponse> WorkflowSteps { get; init; } = [];

        public List<EdgeResponse> Edges { get; init; } = [];
    }

    public sealed record ChainedResponse(Guid ExecutionId, Guid WorkflowId, string Status);

    public sealed record VersionStepResponse(int Order, string? Name, string ActionType);

    public sealed record EdgeResponse(int From, int To, string? Label);

    public sealed record StepResponse(
        Guid Id,
        int StepOrder,
        string ActionType,
        string Status,
        int Attempts,
        int FailedAttempts,
        string? Output,
        string? Error,
        DateTimeOffset StartedAt,
        DateTimeOffset? CompletedAt);
}
