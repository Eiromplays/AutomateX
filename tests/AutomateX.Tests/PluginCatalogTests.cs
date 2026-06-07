using System.Security.Cryptography;
using AutomateX.Engine.Plugins;
using Xunit;

namespace AutomateX.Tests;

// Catalog installs verify content hashes BEFORE anything touches disk —
// a tampered or truncated download never reaches the plugins folder.
public sealed class PluginCatalogTests
{
    private const string Valid =
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
    public void Parse_reads_entries()
    {
        var entries = PluginCatalog.Parse(Valid);

        var entry = Assert.Single(entries);
        Assert.Equal("AutomateX.Plugins.Matrix", entry.Name);
        Assert.Equal("2.8.0", entry.Version);
        Assert.StartsWith("https://", entry.Url);
        Assert.Equal("abc123", entry.Sha256);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"plugins":[{"name":"x"}]}""")]
    public void Malformed_catalogs_are_rejected(string json) =>
        Assert.Throws<InvalidOperationException>(() => PluginCatalog.Parse(json));

    [Fact]
    public void Verify_accepts_matching_content()
    {
        var content = "plugin-bytes"u8.ToArray();
        var sha = Convert.ToHexString(SHA256.HashData(content));

        Assert.True(PluginCatalog.Verify(content, sha));
        Assert.True(PluginCatalog.Verify(content, sha.ToLowerInvariant()));
    }

    [Fact]
    public void Verify_rejects_tampered_content()
    {
        var content = "plugin-bytes"u8.ToArray();
        var sha = Convert.ToHexString(SHA256.HashData(content));

        Assert.False(PluginCatalog.Verify("plugin-bytes!"u8.ToArray(), sha));
        Assert.False(PluginCatalog.Verify(content, "deadbeef"));
    }
}
