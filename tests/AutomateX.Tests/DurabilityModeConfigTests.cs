using AutomateX.Engine;
using Microsoft.Extensions.Configuration;
using Wolverine;
using Xunit;

namespace AutomateX.Tests;

// v5.0 m1: cluster mode is config-driven. Balanced (multi-node: leader election + distributed agents) is the
// default; Solo (single node) is the dev/test path. AddAutomateXEngine resolves it while UseWolverine is
// configured, so the resolution seam is unit-tested directly rather than by booting a host.
public sealed class DurabilityModeConfigTests
{
    private static IConfiguration Config(string? mode) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(mode is null
                ? []
                : new Dictionary<string, string?> { ["Engine:DurabilityMode"] = mode })
            .Build();

    [Fact]
    public void Defaults_to_Balanced_when_unset()
    {
        Assert.Equal(DurabilityMode.Balanced, AutomateXEngine.ResolveDurabilityMode(Config(null)));
    }

    [Fact]
    public void Empty_value_falls_back_to_Balanced()
    {
        Assert.Equal(DurabilityMode.Balanced, AutomateXEngine.ResolveDurabilityMode(Config("")));
    }

    [Fact]
    public void Solo_is_honoured()
    {
        Assert.Equal(DurabilityMode.Solo, AutomateXEngine.ResolveDurabilityMode(Config("Solo")));
    }

    [Fact]
    public void Mode_is_case_insensitive()
    {
        Assert.Equal(DurabilityMode.Solo, AutomateXEngine.ResolveDurabilityMode(Config("solo")));
        Assert.Equal(DurabilityMode.Balanced, AutomateXEngine.ResolveDurabilityMode(Config("BALANCED")));
    }

    [Fact]
    public void Unrecognised_value_fails_loudly()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => AutomateXEngine.ResolveDurabilityMode(Config("Clustered")));
        Assert.Contains("Clustered", ex.Message);
    }
}
