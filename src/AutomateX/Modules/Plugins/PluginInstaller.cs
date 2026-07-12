using AutomateX.Database;
using AutomateX.Engine.Plugins;
using AutomateX.Modules.Plugins.Features;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Plugins;

// Shared install IO for the catalog install endpoint (m4) and the auto-updater (m5): verify sha256,
// extract, reload (incl. the v5.0 cross-node propagation), and record/refresh provenance. The trust /
// permission / compatibility decision is the CALLER's — this only runs once the caller has approved it.
public sealed class PluginInstaller(
    PluginAssemblies plugins, PluginReloadCoordinator coordinator, AutomateXDbContext dbContext)
{
    public sealed record Result(string? PreviousFingerprint, string? Fingerprint);

    public async Task<Result> InstallAsync(
        byte[] content, PluginManifest manifest, CatalogVersion version,
        Guid? sourceId, string? publisher, string installedBy, CancellationToken ct)
    {
        if (!PluginCatalog.Verify(content, version.Sha256))
        {
            throw new InvalidOperationException($"Checksum mismatch for '{manifest.Name}' — refusing to install.");
        }

        var previous = PluginFingerprints.Find(plugins, "global", Guid.Empty, manifest.Name);

        using (var stream = new MemoryStream(content))
        {
            PluginArchive.Extract(stream, manifest.Name, plugins.GlobalRoot);
        }

        await coordinator.ReloadAndNotifyAsync(ct);
        var fingerprint = PluginFingerprints.Find(plugins, "global", Guid.Empty, manifest.Name);

        var existing = await dbContext.InstalledPlugins
            .FirstOrDefaultAsync(x => x.WorkspaceId == Guid.Empty && x.Name == manifest.Name, ct);
        if (existing is null)
        {
            dbContext.InstalledPlugins.Add(InstalledPlugin.Record(
                Guid.Empty, manifest.Name, manifest.Id, version.Version, version.Sha256, sourceId, publisher, installedBy));
        }
        else
        {
            existing.Refresh(manifest.Id, version.Version, version.Sha256, sourceId, publisher, installedBy);
        }

        await dbContext.SaveChangesAsync(ct);
        return new Result(previous, fingerprint);
    }
}
