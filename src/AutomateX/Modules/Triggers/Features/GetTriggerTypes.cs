using AutomateX.Engine.Triggers;
using AutomateX.Modules.Workspaces;
using FastEndpoints;

namespace AutomateX.Modules.Triggers.Features;

public static class GetTriggerTypes
{
    public sealed class Endpoint(TriggerRegistry registry, WorkspaceAccess access) : EndpointWithoutRequest<List<Response>>
    {
        public override void Configure()
        {
            Get("trigger-types");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Viewer, ct) is null)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            List<Response> types =
            [
                new(TriggerTypes.Cron, "Cron", "Runs on a schedule.", "builtin", null),
                new(TriggerTypes.Webhook, "Webhook", "Fires on an authenticated HTTP call.", "builtin", null),
                new(TriggerTypes.Workflow, "Workflow (chain)", "Fires when another workflow finishes.", "builtin", null),
                new(TriggerTypes.OnFailure, "On failure", "Fires when an execution in this workspace fails.", "builtin", null),
                .. registry.Descriptors
                    .OrderBy(x => x.Type)
                    .Select(x => new Response(
                        x.Type, x.DisplayName, x.Description, x.Source, x.ConfigSchema?.ToJsonString())),
            ];

            await Send.OkAsync(types, ct);
        }
    }

    public sealed record Response(
        string Type,
        string DisplayName,
        string? Description,
        string Source,
        string? ConfigSchema);
}
