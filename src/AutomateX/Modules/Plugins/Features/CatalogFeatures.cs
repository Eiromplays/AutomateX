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

            using var http = httpClientFactory.CreateClient();
            var sources = await PluginCatalogFetch.ReadAllAsync(http, options.Value, dbContext, ct);

            var installedNames = plugins.EnumeratePaths()
                .Where(p => p.WorkspaceId is null)
                .Select(x => x.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var provenance = await dbContext.InstalledPlugins
                .AsNoTracking()
                .Where(x => x.WorkspaceId == Guid.Empty)
                .Select(x => new { x.Name, x.Version, x.AutoUpdate })
                .ToListAsync(ct);
            var installedVersions = provenance.ToDictionary(x => x.Name, x => x.Version, StringComparer.OrdinalIgnoreCase);
            var autoUpdateNames = provenance
                .Where(x => x.AutoUpdate)
                .Select(x => x.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var listings = CatalogAggregator.Merge(
                sources.Select(x => x.Result), installedNames, installedVersions, autoUpdateNames);

            await Send.OkAsync(
                new Response(
                    options.Value.AllowPluginUpload,
                    listings.Select(ToEntry).ToList(),
                    sources.Select(x => new SourceStatus(
                        x.Name, x.Url, x.Result.Index is not null, x.Result.Trust.ToString(), x.Error)).ToList()),
                ct);
        }

        private static CatalogEntry ToEntry(CatalogListing listing) => new(
            listing.Id, listing.Name, listing.DisplayName, listing.LatestVersion, listing.Description,
            listing.Author, [.. listing.Categories], listing.Icon, listing.SourceName, listing.Trust.ToString(),
            listing.Installed, listing.InstalledVersion, listing.UpdateAvailable, listing.AutoUpdate,
            listing.Capabilities.Actions.Count, listing.Capabilities.Triggers.Count,
            listing.Capabilities.ConnectionTypes.Count, [.. listing.Permissions.Egress],
            [.. listing.Versions.Select(v => v.Version)]);
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
        bool AutoUpdate,
        int Actions,
        int Triggers,
        int Connections,
        List<string> Egress,
        List<string> Versions);

    public sealed record SourceStatus(string Name, string Url, bool Ok, string Trust, string? Error);

    public sealed record Response(bool InstallEnabled, List<CatalogEntry> Entries, List<SourceStatus> Sources);
}

// Install / update from the catalog. An installed plugin is native code with full host trust, so on top of
// the AllowPluginUpload master switch: sha256 verifies the artifact (integrity), the source's signature sets
// the trust tier, an Untrusted source needs an instance admin + acknowledgement, declared permissions need an
// acknowledgement, and minAutomateXVersion is enforced — all BEFORE anything touches disk. Extract + reload
// (incl. the v5.0 cross-node propagation) are unchanged; provenance is recorded for update/auto-update.
public static class InstallCatalogPlugin
{
    public sealed class Endpoint(
        IHttpClientFactory httpClientFactory,
        IOptions<EngineOptions> options,
        WorkspaceAccess access,
        Audit.IAuditSink audit,
        AutomateXDbContext dbContext,
        PluginInstaller installer) : Endpoint<Request, Response>
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
            var sources = await PluginCatalogFetch.ReadAllAsync(http, options.Value, dbContext, ct);

            if (PluginCatalogResolve.Best(sources, req.Id, req.Name) is not { } resolved)
            {
                ThrowError($"'{req.Id ?? req.Name}' is not in any enabled catalog.");
                return;
            }

            var manifest = resolved.Plugin.Manifest;
            if (resolved.Plugin.Select(req.Version) is not { } version)
            {
                ThrowError($"{manifest.DisplayName} has no version '{req.Version}'.");
                return;
            }

            var minVersion = version.MinAutomateXVersion ?? manifest.MinAutomateXVersion;
            if (!PluginCompatibility.IsSupported(AppVersion.Current, minVersion))
            {
                ThrowError($"{manifest.DisplayName} {version.Version} requires AutomateX {minVersion} or newer.");
            }

            var trust = resolved.Source.Result.Trust;
            switch (InstallGate.Evaluate(trust, access.IsInstanceAdmin(HttpContext), req.Acknowledge, manifest.Permissions.Egress.Count > 0))
            {
                case InstallDecision.RequiresInstanceAdmin:
                    await Send.ForbiddenAsync(ct);
                    return;
                case InstallDecision.Refused:
                    ThrowError($"'{manifest.DisplayName}' failed its signature check — refusing to install.");
                    return;
                case InstallDecision.NeedsAcknowledgement:
                    ThrowError(ConsentMessage(manifest, resolved.Source.Name, trust));
                    return;
                case InstallDecision.Allowed:
                    break;
            }

            byte[] content;
            try
            {
                content = await http.GetByteArrayAsync(version.Url, ct);
            }
            catch (HttpRequestException exception)
            {
                ThrowError($"Download failed: {exception.Message}");
                return;
            }

            PluginInstaller.Result result;
            try
            {
                result = await installer.InstallAsync(
                    content, manifest, version, resolved.Source.SourceId,
                    resolved.Source.Result.Index?.Publisher?.Name, WorkspaceAccess.GetActor(HttpContext), ct);
            }
            catch (InvalidOperationException exception)
            {
                ThrowError(exception.Message);
                return;
            }

            await audit.RecordAsync(
                "plugin.install", null, WorkspaceAccess.GetActor(HttpContext),
                "plugin", manifest.Name, $"{manifest.DisplayName} {version.Version} ({trust}, {resolved.Source.Name})", ct);

            await Send.OkAsync(
                new Response(manifest.Name, version.Version, trust.ToString(), result.PreviousFingerprint, result.Fingerprint), ct);
        }

        private static string ConsentMessage(PluginManifest manifest, string sourceName, CatalogTrust trust)
        {
            List<string> reasons = [];
            if (trust != CatalogTrust.Verified)
            {
                reasons.Add($"it comes from an untrusted source ('{sourceName}')");
            }

            if (manifest.Permissions.Egress.Count > 0)
            {
                reasons.Add($"it requests network access to: {string.Join(", ", manifest.Permissions.Egress)}");
            }

            return $"{manifest.DisplayName} needs your consent — {string.Join("; ", reasons)}. "
                + "Re-install with acknowledge=true to proceed.";
        }
    }

    // Resolve by id (preferred) or package name; optional version pin; acknowledge to consent to an
    // untrusted source and/or declared permissions.
    public sealed record Request(string? Id, string? Name, string? Version, bool Acknowledge);

    public sealed record Response(string Name, string Version, string Trust, string? PreviousFingerprint, string? Fingerprint);
}
