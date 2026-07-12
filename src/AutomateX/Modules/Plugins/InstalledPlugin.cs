namespace AutomateX.Modules.Plugins;

// Provenance for a catalog-installed plugin — where it came from and at what version — so "update
// available", auto-update (m5), and "where from" are facts, not name-guesses. Best-effort: a plugin
// uploaded as a zip or dropped on disk has no row (and the browse view falls back to a name match).
// Global installs use WorkspaceId == Guid.Empty (the plugin system's global sentinel); catalog install is
// global-only today. Written by the install flow in m4; read here for update detection.
public sealed class InstalledPlugin
{
    private InstalledPlugin()
    {
    }

    public Guid Id { get; private set; }

    public Guid WorkspaceId { get; private set; } // Guid.Empty = global

    public string Name { get; private set; } = null!; // package/dll name

    public string PluginId { get; private set; } = null!; // catalog id

    public string Version { get; private set; } = null!;

    public string Sha256 { get; private set; } = null!;

    public Guid? SourceId { get; private set; }

    public string? Publisher { get; private set; }

    public DateTimeOffset InstalledAt { get; private set; }

    public string InstalledBy { get; private set; } = null!;

    // Opt-in: when true, the auto-updater keeps this plugin on the latest compatible, same-major, Verified
    // version. Preserved across updates (Refresh doesn't touch it).
    public bool AutoUpdate { get; private set; }

    public static InstalledPlugin Record(
        Guid workspaceId, string name, string pluginId, string version, string sha256,
        Guid? sourceId, string? publisher, string installedBy) => new()
    {
        Id = Guid.CreateVersion7(),
        WorkspaceId = workspaceId,
        Name = name,
        PluginId = pluginId,
        Version = version,
        Sha256 = sha256,
        SourceId = sourceId,
        Publisher = publisher,
        InstalledAt = DateTimeOffset.UtcNow,
        InstalledBy = installedBy,
    };

    public void Refresh(
        string pluginId, string version, string sha256, Guid? sourceId, string? publisher, string installedBy)
    {
        PluginId = pluginId;
        Version = version;
        Sha256 = sha256;
        SourceId = sourceId;
        Publisher = publisher;
        InstalledAt = DateTimeOffset.UtcNow;
        InstalledBy = installedBy;
    }

    public void SetAutoUpdate(bool enabled) => AutoUpdate = enabled;
}
