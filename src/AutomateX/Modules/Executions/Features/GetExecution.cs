using AutomateX.Database;
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
                        .ToList()))
                .FirstOrDefaultAsync(ct);

            if (execution is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            await Send.OkAsync(execution, ct);
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
        List<StepResponse> Steps);

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
