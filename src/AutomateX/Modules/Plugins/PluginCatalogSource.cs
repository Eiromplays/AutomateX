namespace AutomateX.Modules.Plugins;

// An operator-added catalog source (npm/nuget-style "add a source"). The first-party catalog is served
// implicitly from EngineOptions.PluginCatalogUrl; these rows are the *additional* catalogs — public,
// third-party, or a private one on a LAN host. TrustedKey is the pinned minisign public key: null means
// the source is Untrusted (its plugins list, but installing them needs an explicit acknowledgement).
public sealed class PluginCatalogSource
{
    private PluginCatalogSource()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string Url { get; private set; } = null!;

    public string? TrustedKey { get; private set; }

    public bool Enabled { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static PluginCatalogSource Create(string name, string url, string? trustedKey) => new()
    {
        Id = Guid.CreateVersion7(),
        Name = name,
        Url = url,
        TrustedKey = trustedKey,
        Enabled = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    // Applies only the provided fields. To clear a pinned key, delete and re-add the source.
    public void Update(string? name, string? url, string? trustedKey, bool? enabled)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            Name = name;
        }

        if (!string.IsNullOrWhiteSpace(url))
        {
            Url = url;
        }

        if (!string.IsNullOrWhiteSpace(trustedKey))
        {
            TrustedKey = trustedKey;
        }

        if (enabled is { } value)
        {
            Enabled = value;
        }
    }
}
