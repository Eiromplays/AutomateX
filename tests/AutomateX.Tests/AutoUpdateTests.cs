using AutomateX.Engine.Plugins;
using Xunit;

namespace AutomateX.Tests;

public sealed class AutoUpdateTests
{
    private const string App = "5.0.0";

    [Fact]
    public void Picks_the_highest_compatible_same_major_newer_version()
    {
        var target = AutoUpdate.Choose("1.2.0", [Version("1.4.0"), Version("1.3.0"), Version("1.2.0")], App);

        Assert.Equal("1.4.0", target!.Version);
    }

    [Fact]
    public void Never_bumps_across_a_major()
    {
        var target = AutoUpdate.Choose("1.2.0", [Version("2.0.0"), Version("1.3.0")], App);

        Assert.Equal("1.3.0", target!.Version);
    }

    [Fact]
    public void Skips_versions_that_need_a_newer_app()
    {
        var target = AutoUpdate.Choose(
            "1.2.0", [Version("1.9.0", minApp: "9.0.0"), Version("1.3.0")], App);

        Assert.Equal("1.3.0", target!.Version);
    }

    [Fact]
    public void Returns_null_when_already_current()
    {
        Assert.Null(AutoUpdate.Choose("1.4.0", [Version("1.4.0"), Version("1.3.0")], App));
        Assert.Null(AutoUpdate.Choose("2.0.0", [Version("3.0.0")], App)); // only a major bump available
    }

    private static CatalogVersion Version(string version, string? minApp = null) =>
        new(version, $"https://example.com/{version}.zip", "sha", minApp);
}
