using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Engine.Plugins;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.Extensions.Options;

namespace AutomateX.Modules.Plugins.Features;

public static class GetPluginCatalog
{
    public sealed class Endpoint(
        PluginAssemblies plugins,
        IHttpClientFactory httpClientFactory,
        IOptions<EngineOptions> options,
        WorkspaceAccess access) : EndpointWithoutRequest<Response>
    {
        public override void Configure()
        {
            Get("plugins/catalog");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Viewer, ct) is null)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var url = options.Value.PluginCatalogUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                ThrowError("No plugin catalog configured (Engine__PluginCatalogUrl).");
            }

            string json;
            try
            {
                using var http = httpClientFactory.CreateClient();
                json = await http.GetStringAsync(url, ct);
            }
            catch (HttpRequestException exception)
            {
                ThrowError($"Could not fetch the catalog: {exception.Message}");
                return;
            }

            List<PluginCatalog.Entry> entries;
            try
            {
                entries = PluginCatalog.Parse(json);
            }
            catch (InvalidOperationException exception)
            {
                ThrowError(exception.Message);
                return;
            }

            var installed = plugins.EnumeratePaths()
                .Where(p => p.WorkspaceId is null)
                .Select(x => x.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            await Send.OkAsync(new Response(
                options.Value.AllowPluginUpload,
                entries.Select(x => new CatalogEntry(
                        x.Name, x.Version, x.Description, installed.Contains(x.Name)))
                    .ToList()), ct);
        }
    }

    public sealed record CatalogEntry(string Name, string Version, string? Description, bool Installed);

    public sealed record Response(bool InstallEnabled, List<CatalogEntry> Entries);
}

public static class InstallCatalogPlugin
{
    // Same trust gate as uploads — an installed plugin is code with full host trust.
    // The download is hash-verified against the catalog BEFORE anything touches disk.
    public sealed class Endpoint(
        PluginAssemblies plugins,
        PluginReloader reloader,
        IHttpClientFactory httpClientFactory,
        IOptions<EngineOptions> options,
        WorkspaceAccess access,
        Audit.IAuditSink audit) : Endpoint<Request, Response>
    {
        public override void Configure()
        {
            Post("plugins/catalog/install");
            AllowAnonymous();
        }

        public override async Task HandleAsync(Request req, CancellationToken ct)
        {
            if (!options.Value.AllowPluginUpload)
            {
                ThrowError("Plugin management is disabled. Set Engine__AllowPluginUpload=true to enable it.");
            }

            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Viewer, ct) is null)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            using var http = httpClientFactory.CreateClient();

            PluginCatalog.Entry? entry;
            try
            {
                var catalog = PluginCatalog.Parse(await http.GetStringAsync(options.Value.PluginCatalogUrl, ct));
                entry = catalog.FirstOrDefault(x => string.Equals(x.Name, req.Name, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
            {
                ThrowError($"Could not fetch the catalog: {exception.Message}");
                return;
            }

            if (entry is null)
            {
                ThrowError($"'{req.Name}' is not in the catalog.");
                return;
            }

            byte[] content;
            try
            {
                content = await http.GetByteArrayAsync(entry.Url, ct);
            }
            catch (HttpRequestException exception)
            {
                ThrowError($"Download failed: {exception.Message}");
                return;
            }

            if (!PluginCatalog.Verify(content, entry.Sha256))
            {
                ThrowError($"Checksum mismatch for '{entry.Name}' — refusing to install.");
            }

            var previous = PluginFingerprints.Find(plugins, "global", Guid.Empty, entry.Name);

            try
            {
                using var stream = new MemoryStream(content);
                PluginArchive.Extract(stream, entry.Name, plugins.GlobalRoot);
            }
            catch (InvalidOperationException exception)
            {
                ThrowError(exception.Message);
            }

            reloader.Reload();
            var fingerprint = PluginFingerprints.Find(plugins, "global", Guid.Empty, entry.Name);
            await audit.RecordAsync(
                "plugin.install", null, WorkspaceAccess.GetActor(User),
                "plugin", entry.Name, $"{entry.Name} {entry.Version}", ct);
            await Send.OkAsync(new Response(entry.Name, entry.Version, previous, fingerprint), ct);
        }
    }

    public sealed record Request(string Name);

    public sealed record Response(string Name, string Version, string? PreviousFingerprint, string? Fingerprint);
}
