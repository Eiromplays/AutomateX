using AutomateX.Web;
using Xunit;

namespace AutomateX.Tests;

// Auth cookie session length. Pinned rule: ResolvedSessionLifetime is the
// framework-standard 14 days unless Auth__SessionLifetime sets a positive
// override; a zero/negative value is ignored (an unusable cookie window) and
// falls back to the default.
public sealed class SessionLifetimeTests
{
    [Fact]
    public void Defaults_to_fourteen_days_when_unset()
    {
        Assert.Equal(TimeSpan.FromDays(14), new AuthOptions().ResolvedSessionLifetime);
    }

    [Fact]
    public void Honors_a_configured_positive_value()
    {
        var options = new AuthOptions { SessionLifetime = TimeSpan.FromDays(7) };

        Assert.Equal(TimeSpan.FromDays(7), options.ResolvedSessionLifetime);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Falls_back_to_default_for_non_positive(int hours)
    {
        var options = new AuthOptions { SessionLifetime = TimeSpan.FromHours(hours) };

        Assert.Equal(TimeSpan.FromDays(14), options.ResolvedSessionLifetime);
    }
}
