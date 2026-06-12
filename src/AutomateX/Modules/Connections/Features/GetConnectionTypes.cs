using AutomateX.Engine.Connections;
using AutomateX.Modules.Workspaces;
using FastEndpoints;

namespace AutomateX.Modules.Connections.Features;

public static class GetConnectionTypes
{
    public sealed class Endpoint(ConnectionTypeRegistry registry, WorkspaceAccess access) : EndpointWithoutRequest<List<Response>>
    {
        public override void Configure()
        {
            Get("connection-types");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Viewer, ct) is null)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var types = registry.Descriptors
                .OrderBy(x => x.DisplayName)
                .Select(x => new Response(
                    x.Type,
                    x.DisplayName,
                    x.Description,
                    x.Source,
                    x.Fields.Select(f => new FieldResponse(f.Key, f.Label, f.Secret, f.Required, f.HelpText, f.DocsUrl)).ToList(),
                    x.IsOAuth))
                .ToList();

            await Send.OkAsync(types, ct);
        }
    }

    public sealed record Response(
        string Type, string DisplayName, string? Description, string Source, List<FieldResponse> Fields, bool IsOAuth);

    public sealed record FieldResponse(string Key, string Label, bool Secret, bool Required, string? HelpText, string? DocsUrl);
}
