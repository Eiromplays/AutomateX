using System.Net;
using AutomateX.Plugin.Protocol;
using Xunit;

namespace AutomateX.Tests;

public sealed class EgressGuardTests
{
    // The v4.7 classification table — the single source of truth for both the raw classifier and the
    // empty-allowlist policy path (which must reproduce it exactly).
    public static TheoryData<string, bool> ClassificationTable => new()
    {
        { "127.0.0.1", true },      // loopback
        { "::1", true },            // loopback v6
        { "0.0.0.0", true },        // unspecified
        { "0.1.2.3", true },        // 0.0.0.0/8 "this network"
        { "100.64.0.1", false },    // CGNAT / Tailscale — intentionally allowed
        { "10.1.2.3", true },       // RFC1918
        { "172.16.0.1", true },     // RFC1918 lower bound
        { "172.31.255.255", true }, // RFC1918 upper bound
        { "172.32.0.1", false },    // just outside 172.16/12
        { "192.168.1.1", true },    // RFC1918
        { "169.254.169.254", true },// link-local / cloud metadata
        { "fc00::1", true },        // unique local
        { "fe80::1", true },        // link-local v6
        { "8.8.8.8", false },       // public
        { "1.1.1.1", false },       // public
        { "2606:4700:4700::1111", false }, // public v6
    };

    [Theory]
    [MemberData(nameof(ClassificationTable))]
    public void IsBlockedAddress_classifies(string ip, bool blocked) =>
        Assert.Equal(blocked, EgressGuard.IsBlockedAddress(IPAddress.Parse(ip)));

    // Empty allowlist ⇒ the policy decision is exactly the inverse of the v4.7 block table.
    [Theory]
    [MemberData(nameof(ClassificationTable))]
    public void Empty_policy_reproduces_the_block_table(string ip, bool blocked)
    {
        var policy = new EgressPolicy(blockPrivate: true, allowlist: null);
        Assert.Equal(!blocked, policy.IsConnectionAllowed("host.example", IPAddress.Parse(ip)));
    }

    [Fact]
    public void Guard_off_allows_everything()
    {
        var policy = new EgressPolicy(blockPrivate: false, allowlist: ["10.8.0.0/24"]);
        Assert.True(policy.IsConnectionAllowed("anything", IPAddress.Parse("127.0.0.1")));
        Assert.True(policy.IsConnectionAllowed("anything", IPAddress.Parse("10.9.9.9")));
    }

    [Fact]
    public void Public_targets_are_allowed_regardless_of_allowlist()
    {
        var policy = new EgressPolicy(blockPrivate: true, allowlist: []);
        Assert.True(policy.IsConnectionAllowed("example.com", IPAddress.Parse("8.8.8.8")));
    }

    [Fact]
    public void Allow_hostname_permits_an_otherwise_blocked_host()
    {
        var policy = new EgressPolicy(blockPrivate: true, allowlist: ["metrics.lan"]);

        Assert.True(policy.IsConnectionAllowed("metrics.lan", IPAddress.Parse("10.1.2.3")));
        Assert.True(policy.IsConnectionAllowed("METRICS.LAN", IPAddress.Parse("10.1.2.3"))); // case-insensitive
        Assert.False(policy.IsConnectionAllowed("other.lan", IPAddress.Parse("10.1.2.3")));  // not on the list
    }

    [Theory]
    [InlineData("10.8.0.0/24", "10.8.0.5", true)]    // in v4 range
    [InlineData("10.8.0.0/24", "10.9.0.5", false)]   // outside range, still private → blocked
    [InlineData("fd00::/8", "fd00::1", true)]        // in v6 range
    [InlineData("fd00::/8", "fe80::1", false)]       // outside range, still link-local → blocked
    public void Allow_cidr_permits_an_ip_in_range(string cidr, string ip, bool allowed)
    {
        var policy = new EgressPolicy(blockPrivate: true, allowlist: [cidr]);
        Assert.Equal(allowed, policy.IsConnectionAllowed("host.example", IPAddress.Parse(ip)));
    }

    [Fact]
    public void Malformed_entries_are_dropped_and_surfaced_not_thrown()
    {
        var policy = new EgressPolicy(
            blockPrivate: true, allowlist: ["not a host!!", "999.999.0.0/16", "  ", "10.8.0.0/24", "metrics.lan"]);

        Assert.Contains("not a host!!", policy.InvalidEntries);
        Assert.Contains("999.999.0.0/16", policy.InvalidEntries);
        Assert.DoesNotContain("10.8.0.0/24", policy.InvalidEntries);
        Assert.DoesNotContain("metrics.lan", policy.InvalidEntries);

        // The valid entries alongside the bad ones are still applied.
        Assert.True(policy.IsConnectionAllowed("host.example", IPAddress.Parse("10.8.0.5")));
        Assert.True(policy.IsConnectionAllowed("metrics.lan", IPAddress.Parse("10.1.2.3")));
    }
}
