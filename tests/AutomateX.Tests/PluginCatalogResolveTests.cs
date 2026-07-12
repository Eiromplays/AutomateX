using AutomateX.Engine.Plugins;
using AutomateX.Modules.Plugins;
using Xunit;

namespace AutomateX.Tests;

public sealed class PluginCatalogResolveTests
{
    [Fact]
    public void Resolves_by_id_preferring_a_verified_source()
    {
        var resolved = PluginCatalogResolve.Best(
            [
                Source("untrusted", CatalogTrust.Untrusted, Plugin("automatex.slack", "AutomateX.Plugins.Slack", "9.9.9")),
                Source("first-party", CatalogTrust.Verified, Plugin("automatex.slack", "AutomateX.Plugins.Slack", "1.3.0")),
            ],
            id: "automatex.slack", name: null);

        Assert.NotNull(resolved);
        Assert.Equal("first-party", resolved!.Source.Name);
        Assert.Equal("1.3.0", resolved.Plugin.Latest.Version);
    }

    [Fact]
    public void Resolves_by_package_name_when_no_id_is_given()
    {
        var resolved = PluginCatalogResolve.Best(
            [Source("A", CatalogTrust.Verified, Plugin("automatex.slack", "AutomateX.Plugins.Slack", "1.0.0"))],
            id: null, name: "AutomateX.Plugins.Slack");

        Assert.Equal("automatex.slack", resolved!.Plugin.Manifest.Id);
    }

    [Fact]
    public void Returns_null_when_not_found() =>
        Assert.Null(PluginCatalogResolve.Best(
            [Source("A", CatalogTrust.Verified, Plugin("automatex.slack", "AutomateX.Plugins.Slack", "1.0.0"))],
            id: "acme.missing", name: null));

    [Fact]
    public void Skips_blocked_and_unreachable_sources()
    {
        var resolved = PluginCatalogResolve.Best(
            [
                Source("blocked", CatalogTrust.Blocked, Plugin("automatex.slack", "AutomateX.Plugins.Slack", "2.0.0")),
                new FetchedSource("unreachable", "https://x", null, new CatalogSourceResult("unreachable", CatalogTrust.Untrusted, null), "boom"),
            ],
            id: "automatex.slack", name: null);

        Assert.Null(resolved);
    }

    private static FetchedSource Source(string name, CatalogTrust trust, params CatalogPlugin[] plugins) =>
        new(name, $"https://{name}", null, new CatalogSourceResult(name, trust, new CatalogIndex(2, null, plugins)), null);

    private static CatalogPlugin Plugin(string id, string name, params string[] versions)
    {
        var manifest = new PluginManifest(
            id, name, name, versions[0], null, null, null, null, [], null, null,
            PluginCapabilities.None, PluginPermissions.None);
        var catalogVersions = versions
            .Select(v => new CatalogVersion(v, $"https://example.com/{name}-{v}.zip", "sha", null))
            .ToList();
        return new CatalogPlugin(manifest, catalogVersions);
    }
}
