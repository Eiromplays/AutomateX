using AutomateX.Engine.Plugins;
using Xunit;

namespace AutomateX.Tests;

// ParseIndex reads the v2 catalog (manifest fields + per-plugin versions[]) and still folds the v1
// shape (flat name/version/url/sha256) into a single-version entry, so a legacy catalog keeps working.
public sealed class PluginCatalogIndexTests
{
    private const string V2 =
        """
        {
          "schemaVersion": 2,
          "generated": "2026-07-12T00:00:00Z",
          "publisher": { "name": "AutomateX", "keyId": "AX1" },
          "plugins": [
            {
              "id": "automatex.slack",
              "name": "AutomateX.Plugins.Slack",
              "displayName": "Slack",
              "version": "1.4.0",
              "categories": ["chat"],
              "capabilities": { "actions": ["slack.postMessage"], "triggers": [], "connectionTypes": ["slack"] },
              "permissions": { "egress": ["hooks.slack.com"] },
              "versions": [
                { "version": "1.4.0", "minAutomateXVersion": "5.0.0", "url": "https://example.com/slack-1.4.0.zip", "sha256": "aaa" },
                { "version": "1.3.0", "url": "https://example.com/slack-1.3.0.zip", "sha256": "bbb" }
              ]
            }
          ]
        }
        """;

    // The v1 shape still published by the current package-plugins.sh.
    private const string V1 =
        """
        {
          "generated": "2026-06-07T00:00:00Z",
          "plugins": [
            {
              "name": "AutomateX.Plugins.Matrix",
              "version": "2.8.0",
              "description": "Send Matrix messages.",
              "url": "https://github.com/Eiromplays/AutomateX/releases/download/v2.8.0/AutomateX.Plugins.Matrix.zip",
              "sha256": "abc123"
            }
          ]
        }
        """;

    [Fact]
    public void ParseIndex_reads_v2()
    {
        var index = PluginCatalog.ParseIndex(V2);

        Assert.Equal(2, index.SchemaVersion);
        Assert.NotNull(index.Publisher);
        Assert.Equal("AutomateX", index.Publisher!.Name);
        Assert.Equal("AX1", index.Publisher.KeyId);

        var plugin = Assert.Single(index.Plugins);
        Assert.Equal("automatex.slack", plugin.Manifest.Id);
        Assert.Equal("slack.postMessage", Assert.Single(plugin.Manifest.Capabilities.Actions));
        Assert.Equal("hooks.slack.com", Assert.Single(plugin.Manifest.Permissions.Egress));
        Assert.Equal(2, plugin.Versions.Count);
    }

    [Fact]
    public void ParseIndex_selects_latest_and_pinned_versions()
    {
        var plugin = Assert.Single(PluginCatalog.ParseIndex(V2).Plugins);

        Assert.Equal("1.4.0", plugin.Latest.Version);
        Assert.Equal("5.0.0", plugin.Latest.MinAutomateXVersion);
        Assert.Equal("bbb", plugin.Select("1.3.0")!.Sha256);
        Assert.Null(plugin.Select("9.9.9"));
    }

    [Fact]
    public void ParseIndex_folds_a_v1_catalog()
    {
        var index = PluginCatalog.ParseIndex(V1);

        Assert.Equal(1, index.SchemaVersion);
        Assert.Null(index.Publisher);

        var plugin = Assert.Single(index.Plugins);
        Assert.Equal("automatex.plugins.matrix", plugin.Manifest.Id);
        Assert.Equal("AutomateX.Plugins.Matrix", plugin.Manifest.Name);
        Assert.Equal("AutomateX.Plugins.Matrix", plugin.Manifest.DisplayName);
        Assert.Empty(plugin.Manifest.Capabilities.Actions);

        var version = Assert.Single(plugin.Versions);
        Assert.Equal("2.8.0", version.Version);
        Assert.StartsWith("https://", version.Url);
        Assert.Equal("abc123", version.Sha256);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{ "plugins": [ { "id": "a.b", "name": "A.B", "displayName": "A", "version": "1.0.0", "versions": [] } ] }""")]
    [InlineData("""{ "plugins": [ { "id": "a.b", "name": "A.B", "displayName": "A", "version": "1.0.0", "versions": [ { "version": "1.0.0", "url": "https://x/y.zip" } ] } ] }""")]
    [InlineData("""{ "plugins": [ { "name": "A.B" } ] }""")]
    public void Malformed_indexes_are_rejected(string json) =>
        Assert.Throws<InvalidOperationException>(() => PluginCatalog.ParseIndex(json));
}
