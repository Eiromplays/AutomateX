using System.Net;
using AutomateX.Engine.Plugins;
using AutomateX.Plugin.Protocol;
using Xunit;

namespace AutomateX.Tests;

// Restrictive egress: a plugin that declares specific hosts can reach ONLY those (deny everything else,
// public and private, regardless of the SSRF flag). A wildcard / empty / absent declaration is unrestricted,
// which keeps today's behaviour for plugins that don't opt in.
public sealed class PluginEgressTests
{
    [Fact]
    public void AllowOnly_restricts_to_declared_hosts()
    {
        var policy = new EgressPolicy(blockPrivate: false, allowlist: null, allowOnly: ["hooks.slack.com"]);

        Assert.True(policy.HasRestriction);
        Assert.True(policy.IsConnectionAllowed("hooks.slack.com", IPAddress.Parse("13.1.1.1")));
        Assert.False(policy.IsConnectionAllowed("evil.com", IPAddress.Parse("13.1.1.1")));
        // An undeclared private/metadata target is denied too — the restriction is absolute.
        Assert.False(policy.IsConnectionAllowed("metadata", IPAddress.Parse("169.254.169.254")));
    }

    [Fact]
    public void AllowOnly_supports_cidr()
    {
        var policy = new EgressPolicy(blockPrivate: false, allowlist: null, allowOnly: ["10.0.0.0/8"]);

        Assert.True(policy.IsConnectionAllowed("host.lan", IPAddress.Parse("10.1.2.3")));
        Assert.False(policy.IsConnectionAllowed("host.lan", IPAddress.Parse("11.1.2.3")));
    }

    [Fact]
    public void No_restriction_falls_back_to_the_widen_behaviour()
    {
        var policy = new EgressPolicy(blockPrivate: true, allowlist: null, allowOnly: null);

        Assert.False(policy.HasRestriction);
        Assert.True(policy.IsConnectionAllowed("example.com", IPAddress.Parse("93.184.216.34"))); // public: allowed
        Assert.False(policy.IsConnectionAllowed("metadata", IPAddress.Parse("169.254.169.254"))); // private: blocked
    }

    [Fact]
    public void Egress_allowOnly_keeps_specific_declared_hosts() =>
        Assert.Equal(new[] { "hooks.slack.com" }, PluginEgress.AllowOnly(new[] { "hooks.slack.com" }));

    [Fact]
    public void Egress_allowOnly_is_unrestricted_for_a_wildcard() =>
        Assert.Empty(PluginEgress.AllowOnly(new[] { "hooks.slack.com", "*" }));

    [Fact]
    public void Egress_allowOnly_is_unrestricted_for_an_empty_declaration() =>
        Assert.Empty(PluginEgress.AllowOnly([]));
}
