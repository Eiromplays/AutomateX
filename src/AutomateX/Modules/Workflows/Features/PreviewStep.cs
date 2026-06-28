using System.Text.Json;
using AutomateX.Database;
using AutomateX.Engine.Security;
using AutomateX.Engine.Templating;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Workflows.Features;

// Per-step preview: resolve a step's templated config (sent inline, so the builder can preview unsaved
// edits) against a supplied sample context, tolerantly — the response reports every unresolved
// reference at once. No execution, no side effects, and connection values are masked: a preview shows
// which connection fields a step reads, never the secrets.
public static class PreviewStep
{
    public sealed record SampleContext(JsonElement? TriggerPayload, Dictionary<string, JsonElement>? StepOutputs);

    public sealed record Request(string? ConfigJson, Dictionary<string, int>? StepKeys, SampleContext? SampleContext);

    public sealed record Response(
        JsonElement ResolvedConfig,
        IReadOnlyList<string> Unresolved,
        IReadOnlyList<ConnectionUsage> ConnectionsUsed);

    public sealed class Endpoint(
        AutomateXDbContext dbContext,
        TenantCipher cipher,
        AutomateX.Modules.Variables.VariableLoader variableLoader,
        WorkspaceAccess access) : Endpoint<Request, Response>
    {
        public override void Configure()
        {
            Post("workflows/{id}/preview-step");
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
            if (!await dbContext.Workflows.AnyAsync(w => w.Id == id && w.WorkspaceId == ws, ct))
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(req.ConfigJson))
            {
                ThrowError("configJson is required.");
                return;
            }

            var stepKeys = req.StepKeys ?? [];
            var stepOutputs = MapStepOutputs(req.SampleContext?.StepOutputs, stepKeys);
            var connectionFields = await LoadConnectionFieldsAsync(ws, ct);

            // Variables resolve against the workspace's active environment; secret values are masked.
            var (variableValues, secretNames) = await variableLoader.LoadAsync(
                ws, id, await variableLoader.ActiveEnvironmentAsync(ws, ct), ct);
            var maskedVariables = variableValues.ToDictionary(
                x => x.Key, x => secretNames.Contains(x.Key) ? "******" : x.Value);

            StepPreviewResult preview;
            try
            {
                preview = StepPreview.Build(
                    req.ConfigJson, req.SampleContext?.TriggerPayload, stepOutputs, stepKeys, connectionFields, maskedVariables, id);
            }
            catch (TemplateResolutionException ex)
            {
                ThrowError(ex.Message); // config isn't valid JSON
                return;
            }

            await Send.OkAsync(
                new Response(
                    JsonSerializer.Deserialize<JsonElement>(preview.ResolvedConfig),
                    preview.Unresolved,
                    preview.ConnectionsUsed),
                ct);
        }

        // Sample outputs are keyed by step key or numeric order; map both onto the step's order.
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

        // Live connections give the real field names; undecryptable ones are skipped (refs to them
        // simply surface as unresolved). Values are never read here — masking happens in the core.
        private async Task<Dictionary<string, IReadOnlyList<string>>> LoadConnectionFieldsAsync(
            Guid ws, CancellationToken ct)
        {
            Dictionary<string, IReadOnlyList<string>> fields = [];
            var connections = await dbContext.Connections
                .AsNoTracking()
                .Where(x => x.WorkspaceId == ws)
                .ToListAsync(ct);

            foreach (var connection in connections)
            {
                try
                {
                    var values = JsonSerializer.Deserialize<Dictionary<string, string>>(
                        await cipher.DecryptAsync(connection.EncryptedSecrets, connection.WorkspaceId, ct)) ?? [];
                    fields[connection.Name] = [.. values.Keys];
                }
                catch (SecretCipherException)
                {
                }
            }

            return fields;
        }
    }
}
