using AutomateX.Engine.Plugins;
using Xunit;

namespace AutomateX.Tests;

public sealed class CatalogAggregatorTests
{
    private static readonly HashSet<string> NoneInstalled = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> NoProvenance = new(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Merges_plugins_from_every_source()
    {
        var listings = CatalogAggregator.Merge(
            [
                Source("A", CatalogTrust.Verified, Plugin("automatex.slack", "AutomateX.Plugins.Slack", "1.0.0")),
                Source("B", CatalogTrust.Untrusted, Plugin("acme.jira", "Acme.Jira", "2.0.0")),
            ],
            NoneInstalled, NoProvenance);

        Assert.Equal(2, listings.Count);
        Assert.Contains(listings, x => x.Id == "automatex.slack");
        Assert.Contains(listings, x => x.Id == "acme.jira");
    }

    [Fact]
    public void Dedupes_by_id_keeping_the_highest_version_within_a_trust_tier()
    {
        var listings = CatalogAggregator.Merge(
            [
                Source("A", CatalogTrust.Verified, Plugin("automatex.slack", "AutomateX.Plugins.Slack", "1.3.0")),
                Source("B", CatalogTrust.Verified, Plugin("automatex.slack", "AutomateX.Plugins.Slack", "1.4.0")),
            ],
            NoneInstalled, NoProvenance);

        var listing = Assert.Single(listings);
        Assert.Equal("1.4.0", listing.LatestVersion);
    }

    [Fact]
    public void A_verified_source_wins_over_an_untrusted_one_even_with_a_lower_version()
    {
        var listings = CatalogAggregator.Merge(
            [
                Source("untrusted", CatalogTrust.Untrusted, Plugin("automatex.slack", "AutomateX.Plugins.Slack", "9.9.9")),
                Source("first-party", CatalogTrust.Verified, Plugin("automatex.slack", "AutomateX.Plugins.Slack", "1.3.0")),
            ],
            NoneInstalled, NoProvenance);

        var listing = Assert.Single(listings);
        Assert.Equal(CatalogTrust.Verified, listing.Trust);
        Assert.Equal("1.3.0", listing.LatestVersion);
    }

    [Fact]
    public void Untrusted_only_plugins_are_listed_as_untrusted()
    {
        var listings = CatalogAggregator.Merge(
            [Source("B", CatalogTrust.Untrusted, Plugin("acme.jira", "Acme.Jira", "1.0.0"))],
            NoneInstalled, NoProvenance);

        Assert.Equal(CatalogTrust.Untrusted, Assert.Single(listings).Trust);
    }

    [Fact]
    public void Blocked_and_unreachable_sources_contribute_nothing()
    {
        var listings = CatalogAggregator.Merge(
            [
                Source("blocked", CatalogTrust.Blocked, Plugin("automatex.slack", "AutomateX.Plugins.Slack", "1.0.0")),
                new CatalogSourceResult("unreachable", CatalogTrust.Untrusted, Index: null),
            ],
            NoneInstalled, NoProvenance);

        Assert.Empty(listings);
    }

    [Fact]
    public void Flags_an_available_update_against_provenance()
    {
        var listings = CatalogAggregator.Merge(
            [Source("A", CatalogTrust.Verified, Plugin("automatex.slack", "AutomateX.Plugins.Slack", "1.4.0"))],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AutomateX.Plugins.Slack" },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["AutomateX.Plugins.Slack"] = "1.3.0" });

        var listing = Assert.Single(listings);
        Assert.True(listing.Installed);
        Assert.Equal("1.3.0", listing.InstalledVersion);
        Assert.True(listing.UpdateAvailable);
    }

    [Fact]
    public void Installed_without_provenance_shows_no_update()
    {
        var listings = CatalogAggregator.Merge(
            [Source("A", CatalogTrust.Verified, Plugin("automatex.slack", "AutomateX.Plugins.Slack", "1.4.0"))],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AutomateX.Plugins.Slack" },
            NoProvenance);

        var listing = Assert.Single(listings);
        Assert.True(listing.Installed);
        Assert.Null(listing.InstalledVersion);
        Assert.False(listing.UpdateAvailable);
    }

    [Fact]
    public void Uninstalled_plugins_are_not_flagged_installed()
    {
        var listings = CatalogAggregator.Merge(
            [Source("A", CatalogTrust.Verified, Plugin("automatex.slack", "AutomateX.Plugins.Slack", "1.4.0"))],
            NoneInstalled, NoProvenance);

        Assert.False(Assert.Single(listings).Installed);
    }

    private static CatalogSourceResult Source(string name, CatalogTrust trust, params CatalogPlugin[] plugins) =>
        new(name, trust, new CatalogIndex(2, null, plugins));

    // Versions are newest-first (Versions[0] is Latest).
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
