using AutomateX.Engine.Plugins;
using Xunit;

namespace AutomateX.Tests;

public sealed class SemVerTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1.0.0", "1.1.0")]
    [InlineData("1.9.0", "2.0.0")]
    [InlineData("1.0.0-alpha", "1.0.0")]           // pre-release is lower than its release
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]   // more identifiers wins when the prefix is equal
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-beta")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]  // numeric identifiers compare numerically, not lexically
    [InlineData("1.0.0-rc.1", "1.0.0")]
    public void Compare_orders_lower_before_higher(string lower, string higher)
    {
        Assert.True(SemVer.Compare(lower, higher) < 0);
        Assert.True(SemVer.Compare(higher, lower) > 0);
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.0.0+build.5", "1.0.0")]         // build metadata is ignored
    [InlineData("1.0.0-beta+exp", "1.0.0-beta")]
    public void Compare_treats_equivalent_versions_as_equal(string a, string b) =>
        Assert.Equal(0, SemVer.Compare(a, b));
}
