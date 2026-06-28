using System.Text.Json;
using AutomateX.Database;
using AutomateX.Engine.Security;
using AutomateX.Engine.Templating;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Workflows.Features;

// Per-step preview: resolve a step's templated config against a supplied sample context, tolerantly,
// so the response reports every unresolved reference at once. No execution, no side effects, and
// connection values are masked — a preview shows which connection fields a step reads, never secrets.
public static class PreviewStep
{
    public sealed record SampleContext(JsonElement? TriggerPayload, Dictionary<string, JsonElement>? StepOutputs);

    public sealed record Request(SampleContext? SampleContext);

    public sealed record Response(
        JsonElement ResolvedConfig,
        IReadOnlyList<string> Unresolved,
        IReadOnlyList<ConnectionUsage> ConnectionsUsed);

    public sealed class Endpoint(
        AutomateXDbContext dbContext,
        TenantCipher cipher,
        WorkspaceAccess access) : Endpoint<Request, Response>
    {
        public override void Configure()
        {
            Post("workflows/{id}/versions/{versionId}/steps/{key}/preview");
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
            var versionId = Route<Guid>("versionId");
            var key = Route<string>("key")!;

            var steps = await dbContext.Workflows
                .AsNoTracking()
                .Where(w => w.Id == id && w.WorkspaceId == ws)
                .SelectMany(w => w.Versions.Where(v => v.Id == versionId).SelectMany(v => v.Steps))
                .OrderBy(s => s.Order)
                .Select(s => new { s.Order, s.Key, s.ConfigJson })
                .ToListAsync(ct);

            if (steps.Count == 0)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            var target = steps.FirstOrDefault(s => s.Key == key);
            if (target is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            var stepKeys = steps.ToDictionary(s => s.Key, s => s.Order, StringComparer.Ordinal);
            var stepOutputs = MapStepOutputs(req.SampleContext?.StepOutputs, stepKeys);
            var connectionFields = await LoadConnectionFieldsAsync(ws, ct);

            var preview = StepPreview.Build(
                target.ConfigJson, req.SampleContext?.TriggerPayload, stepOutputs, stepKeys, connectionFields, id);

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
