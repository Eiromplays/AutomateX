using AutomateX.Database;
using AutomateX.Modules.Audit;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Plugins.Features;

// Instance-admin CRUD over the operator's added catalog sources (npm/nuget "add a source"). The first-party
// catalog is implicit (EngineOptions.PluginCatalogUrl) and is not a row here.
public static class ListCatalogSources
{
    public sealed record Response(Guid Id, string Name, string Url, bool HasKey, bool Enabled, DateTimeOffset CreatedAt);

    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access)
        : EndpointWithoutRequest<List<Response>>
    {
        public override void Configure()
        {
            Get("plugins/catalog/sources");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Viewer, ct) is null || !access.IsInstanceAdmin(HttpContext))
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            // Never leak the pinned key back out — expose only whether one is set.
            var sources = await dbContext.PluginCatalogSources
                .AsNoTracking()
                .OrderBy(x => x.Name)
                .Select(x => new Response(x.Id, x.Name, x.Url, x.TrustedKey != null, x.Enabled, x.CreatedAt))
                .ToListAsync(ct);

            await Send.OkAsync(sources, ct);
        }
    }
}

public static class AddCatalogSource
{
    public sealed record Request(string? Name, string? Url, string? TrustedKey);

    public sealed record Response(Guid Id);

    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access, IAuditSink audit)
        : Endpoint<Request, Response>
    {
        public override void Configure()
        {
            Post("plugins/catalog/sources");
            AllowAnonymous();
        }

        public override async Task HandleAsync(Request req, CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Viewer, ct) is null || !access.IsInstanceAdmin(HttpContext))
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(req.Name))
            {
                ThrowError("A source name is required.");
            }

            if (!IsHttpUrl(req.Url))
            {
                ThrowError("A source url must be an absolute http(s) URL.");
            }

            var source = PluginCatalogSource.Create(req.Name!.Trim(), req.Url!.Trim(), req.TrustedKey?.Trim());
            dbContext.PluginCatalogSources.Add(source);

            try
            {
                await dbContext.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                ThrowError("A catalog source with that URL already exists.");
            }

            await audit.RecordAsync(
                "plugin.catalog.source.add", null, WorkspaceAccess.GetActor(HttpContext),
                "plugin-catalog-source", source.Id.ToString(), source.Name, ct);
            await Send.OkAsync(new Response(source.Id), ct);
        }

        private static bool IsHttpUrl(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}

public static class UpdateCatalogSource
{
    public sealed record Request(string? Name, string? Url, string? TrustedKey, bool? Enabled);

    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access, IAuditSink audit)
        : Endpoint<Request>
    {
        public override void Configure()
        {
            Put("plugins/catalog/sources/{id}");
            AllowAnonymous();
        }

        public override async Task HandleAsync(Request req, CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Viewer, ct) is null || !access.IsInstanceAdmin(HttpContext))
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var source = await dbContext.PluginCatalogSources.FirstOrDefaultAsync(x => x.Id == Route<Guid>("id"), ct);
            if (source is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            source.Update(req.Name, req.Url, req.TrustedKey, req.Enabled);
            await dbContext.SaveChangesAsync(ct);

            await audit.RecordAsync(
                "plugin.catalog.source.update", null, WorkspaceAccess.GetActor(HttpContext),
                "plugin-catalog-source", source.Id.ToString(), source.Name, ct);
            await Send.NoContentAsync(ct);
        }
    }
}

public static class DeleteCatalogSource
{
    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access, IAuditSink audit)
        : EndpointWithoutRequest
    {
        public override void Configure()
        {
            Delete("plugins/catalog/sources/{id}");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Viewer, ct) is null || !access.IsInstanceAdmin(HttpContext))
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var id = Route<Guid>("id");
            var source = await dbContext.PluginCatalogSources.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (source is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            dbContext.PluginCatalogSources.Remove(source);
            await dbContext.SaveChangesAsync(ct);

            await audit.RecordAsync(
                "plugin.catalog.source.delete", null, WorkspaceAccess.GetActor(HttpContext),
                "plugin-catalog-source", id.ToString(), source.Name, ct);
            await Send.NoContentAsync(ct);
        }
    }
}
