using AutomateX.Engine.Actions;
using FastEndpoints;

namespace AutomateX.Modules.Actions.Features;

public static class GetActions
{
    public sealed class Endpoint(ActionRegistry registry) : EndpointWithoutRequest<List<Response>>
    {
        public override void Configure()
        {
            Get("actions");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            var actions = registry.Descriptors
                .OrderBy(x => x.Type)
                .Select(x => new Response(
                    x.Type,
                    x.DisplayName,
                    x.Description,
                    x.Source,
                    x.ConfigSchema?.ToJsonString(),
                    x.ResultSchema?.ToJsonString()))
                .ToList();

            await Send.OkAsync(actions, ct);
        }
    }

    public sealed record Response(
        string Type,
        string DisplayName,
        string? Description,
        string Source,
        string? ConfigSchema,
        string? ResultSchema);
}
