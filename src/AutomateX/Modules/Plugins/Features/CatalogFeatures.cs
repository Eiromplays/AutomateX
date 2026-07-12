using System.Text;
using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Engine.Plugins;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutomateX.Modules.Plugins.Features;

// Browse: aggregate every enabled source (the implicit first-party catalog from PluginCatalogUrl plus the
// operator's added sources), verify each signature, dedupe by id, and label trust / installed /
// update-available. An unreachable or unparseable source degrades — the rest of the catalog still lists.
public static class GetPluginCatalog
{
    public sealed class Endpoint(
        PluginAssemblies plugins,
        IHttpClientFactory httpClientFactory,
        IOptions<EngineOptions> options,
        WorkspaceAccess access,
        AutomateXDbContext dbContext) : EndpointWithoutRequest<Response>
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

            List<(string Name, string Url, string? Key)> sources = [];
            if (!string.IsNullOrWhiteSpace(options.Value.PluginCatalogUrl))
            {
                sources.Add(("AutomateX", options.Value.PluginCatalogUrl, CatalogKeys.FirstParty));
            }

            var added = await dbContext.PluginCatalogSources
                .AsNoTracking()
                .Where(x => x.Enabled)
                .Select(x => new { x.Name, x.Url, x.TrustedKey })
                .ToListAsync(ct);
            sources.AddRange(added.Select(x => (x.Name, x.Url, (string?)x.TrustedKey)));

            using var http = httpClientFactory.CreateClient();
            List<(CatalogSourceResult Result, string Url, string? Error)> fetched = [];
            foreach (var (name, url, key) in sources)
            {
                fetched.Add(await ReadAsync(http, name, url, key, ct));
            }

            var installedNames = plugins.EnumeratePaths()
                .Where(p => p.WorkspaceId is null)
                .Select(x => x.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var installedVersions = (await dbContext.InstalledPlugins
                    .AsNoTracking()
                    .Where(x => x.WorkspaceId == Guid.Empty)
                    .Select(x => new { x.Name, x.Version })
                    .ToListAsync(ct))
                .ToDictionary(x => x.Name, x => x.Version, StringComparer.OrdinalIgnoreCase);

            var listings = CatalogAggregator.Merge(
                fetched.Select(x => x.Result), installedNames, installedVersions);

            await Send.OkAsync(
                new Response(
                    options.Value.AllowPluginUpload,
                    listings.Select(ToEntry).ToList(),
                    fetched.Select(x => new SourceStatus(
                        x.Result.SourceName, x.Url, x.Result.Index is not null, x.Result.Trust.ToString(), x.Error)).ToList()),
                ct);
        }

        // Fetch the index + its detached .minisig, evaluate trust. Any transport/parse failure degrades the
        // source (null index) rather than failing the whole catalog.
        private static async Task<(CatalogSourceResult Result, string Url, string? Error)> ReadAsync(
            HttpClient http, string name, string url, string? key, CancellationToken ct)
        {
            try
            {
                var bytes = await http.GetByteArrayAsync(url, ct);
                var index = PluginCatalog.ParseIndex(Encoding.UTF8.GetString(bytes));

                string? signature = null;
                try
                {
                    signature = await http.GetStringAsync(url + ".minisig", ct);
                }
                catch (HttpRequestException)
                {
                    // Unsigned source — it stays Untrusted.
                }

                return (new CatalogSourceResult(name, CatalogSignature.Evaluate(bytes, signature, key), index), url, null);
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                return (new CatalogSourceResult(name, CatalogTrust.Untrusted, null), url, exception.Message);
            }
        }

        private static CatalogEntry ToEntry(CatalogListing listing) => new(
            listing.Id, listing.Name, listing.DisplayName, listing.LatestVersion, listing.Description,
            listing.Author, [.. listing.Categories], listing.Icon, listing.SourceName, listing.Trust.ToString(),
            listing.Installed, listing.InstalledVersion, listing.UpdateAvailable,
            listing.Capabilities.Actions.Count, listing.Capabilities.Triggers.Count,
            listing.Capabilities.ConnectionTypes.Count, [.. listing.Permissions.Egress]);
    }

    // Name/Version/Description/Installed are kept for the current UI; the rest is the m8 browse surface.
    public sealed record CatalogEntry(
        string Id,
        string Name,
        string DisplayName,
        string Version,
        string? Description,
        string? Author,
        List<string> Categories,
        string? Icon,
        string Source,
        string Trust,
        bool Installed,
        string? InstalledVersion,
        bool UpdateAvailable,
        int Actions,
        int Triggers,
        int Connections,
        List<string> Egress);

    public sealed record SourceStatus(string Name, string Url, bool Ok, string Trust, string? Error);

    public sealed record Response(bool InstallEnabled, List<CatalogEntry> Entries, List<SourceStatus> Sources);
}

public static class InstallCatalogPlugin
{
    // Same trust gate as uploads — an installed plugin is code with full host trust.
    // The download is hash-verified against the catalog BEFORE anything touches disk.
    public sealed class Endpoint(
        PluginAssemblies plugins,
        PluginReloadCoordinator coordinator,
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

            await coordinator.ReloadAndNotifyAsync(ct);
            var fingerprint = PluginFingerprints.Find(plugins, "global", Guid.Empty, entry.Name);
            await audit.RecordAsync(
                "plugin.install", null, WorkspaceAccess.GetActor(HttpContext),
                "plugin", entry.Name, $"{entry.Name} {entry.Version}", ct);
            await Send.OkAsync(new Response(entry.Name, entry.Version, previous, fingerprint), ct);
        }
    }

    public sealed record Request(string Name);

    public sealed record Response(string Name, string Version, string? PreviousFingerprint, string? Fingerprint);
}
