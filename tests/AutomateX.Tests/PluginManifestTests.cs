using AutomateX.Engine.Plugins;
using Xunit;

namespace AutomateX.Tests;

// The manifest is the plugin's own schema — authored, strictly validated. It carries the plugin's
// independent version (not the API's), declared capabilities, and required permissions.
public sealed class PluginManifestTests
{
    private const string Full =
        """
        {
          "schemaVersion": 1,
          "id": "automatex.slack",
          "name": "AutomateX.Plugins.Slack",
          "displayName": "Slack",
          "version": "1.4.0",
          "description": "Post messages to a Slack channel.",
          "author": "AutomateX",
          "homepage": "https://example.com",
          "license": "MIT",
          "categories": ["notifications", "chat"],
          "icon": "slack",
          "minAutomateXVersion": "5.0.0",
          "capabilities": { "actions": ["slack.postMessage"], "triggers": [], "connectionTypes": ["slack"] },
          "permissions": { "egress": ["hooks.slack.com"] }
        }
        """;

    [Fact]
    public void Parse_reads_a_full_manifest()
    {
        var manifest = PluginManifest.Parse(Full);

        Assert.Equal("automatex.slack", manifest.Id);
        Assert.Equal("AutomateX.Plugins.Slack", manifest.Name);
        Assert.Equal("Slack", manifest.DisplayName);
        Assert.Equal("1.4.0", manifest.Version);
        Assert.Equal("5.0.0", manifest.MinAutomateXVersion);
        Assert.Equal(new[] { "notifications", "chat" }, manifest.Categories);
        Assert.Equal("slack.postMessage", Assert.Single(manifest.Capabilities.Actions));
        Assert.Empty(manifest.Capabilities.Triggers);
        Assert.Equal("slack", Assert.Single(manifest.Capabilities.ConnectionTypes));
        Assert.Equal("hooks.slack.com", Assert.Single(manifest.Permissions.Egress));
    }

    [Fact]
    public void Parse_defaults_optional_fields()
    {
        var manifest = PluginManifest.Parse(
            """{ "id": "acme.tool", "name": "Acme.Tool", "displayName": "Acme Tool", "version": "1.0.0" }""");

        Assert.Null(manifest.Description);
        Assert.Null(manifest.MinAutomateXVersion);
        Assert.Empty(manifest.Categories);
        Assert.Empty(manifest.Capabilities.Actions);
        Assert.Empty(manifest.Permissions.Egress);
    }

    [Fact]
    public void Parse_trims_string_fields()
    {
        var manifest = PluginManifest.Parse(
            """{ "id": "acme.tool", "name": "Acme.Tool", "displayName": "  Acme Tool  ", "version": "1.0.0" }""");

        Assert.Equal("Acme Tool", manifest.DisplayName);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "name": "Acme.Tool", "displayName": "Acme", "version": "1.0.0" }""")]      // missing id
    [InlineData("""{ "id": "acme.tool", "displayName": "Acme", "version": "1.0.0" }""")]        // missing name
    [InlineData("""{ "id": "acme.tool", "name": "Acme.Tool", "version": "1.0.0" }""")]          // missing displayName
    [InlineData("""{ "id": "acme.tool", "name": "Acme.Tool", "displayName": "Acme" }""")]       // missing version
    [InlineData("""{ "id": "acme", "name": "Acme.Tool", "displayName": "Acme", "version": "1.0.0" }""")]       // id not reverse-dns
    [InlineData("""{ "id": "acme.tool", "name": "Acme/Tool", "displayName": "Acme", "version": "1.0.0" }""")]  // bad package name
    [InlineData("""{ "id": "acme.tool", "name": "Acme.Tool", "displayName": "Acme", "version": "1.0" }""")]    // bad semver
    [InlineData("""{ "id": "acme.tool", "name": "Acme.Tool", "displayName": "Acme", "version": "1.0.0", "minAutomateXVersion": "five" }""")]
    public void Malformed_manifests_are_rejected(string json) =>
        Assert.Throws<InvalidOperationException>(() => PluginManifest.Parse(json));
}
