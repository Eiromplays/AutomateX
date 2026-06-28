using System.Text.Json;
using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Modules.Audit;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Workflows.Features;

// Opt-in real single-step run: executes ONE leaf action for real with the step's (inline) config
// resolved against a sample context and LIVE connections. Real side effects, so it requires
// confirm=true, is editor-gated, refuses control-flow nodes (handled in the runner), and is audited.
public static class TestStep
{
    public sealed record Request(
        string? ConfigJson,
        string? ActionType,
        Dictionary<string, int>? StepKeys,
        PreviewStep.SampleContext? SampleContext,
        bool Confirm);

    public sealed record Response(bool Ok, JsonElement? Output, string? Error);

    public sealed class Endpoint(
        AutomateXDbContext dbContext,
        StepTestRunner runner,
        IAuditSink audit,
        WorkspaceAccess access) : Endpoint<Request, Response>
    {
        public override void Configure()
        {
            Post("workflows/{id}/test-step");
            AllowAnonymous();
        }

        public override async Task HandleAsync(Request req, CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Editor, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            if (!req.Confirm)
            {
                ThrowError("Set confirm=true — a real run executes the action with real side effects.");
            }

            if (string.IsNullOrWhiteSpace(req.ConfigJson) || string.IsNullOrWhiteSpace(req.ActionType))
            {
                ThrowError("configJson and actionType are required.");
                return;
            }

            var id = Route<Guid>("id");
            if (!await dbContext.Workflows.AnyAsync(w => w.Id == id && w.WorkspaceId == ws, ct))
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            var stepKeys = req.StepKeys ?? [];
            var stepOutputs = MapStepOutputs(req.SampleContext?.StepOutputs, stepKeys);

            var result = await runner.RunAsync(
                ws, id, req.ActionType, req.ConfigJson, stepKeys, req.SampleContext?.TriggerPayload, stepOutputs, ct);

            await audit.RecordAsync(
                "step.test", ws, WorkspaceAccess.GetActor(User), "workflow", id.ToString(), req.ActionType, ct);

            await Send.OkAsync(new Response(result.Ok, result.Output, result.Error), ct);
        }

        private static Dictionary<int, JsonElement> MapStepOutputs(
            Dictionary<string, JsonElement>? outputs, IReadOnlyDictionary<string, int> stepKeys)
        {
            Dictionary<int, JsonElement> mapped = [];
            foreach (var (idKey, value) in outputs ?? [])
            {
                int? order = int.TryParse(idKey, out var parsed)
                    ? parsed
                    : stepKeys.TryGetValue(idKey, out var keyed) ? keyed : null;
                if (order is { } resolved)
                {
                    mapped[resolved] = value;
                }
            }

            return mapped;
        }
    }
}
