using System.Net;
using AutomateX.Modules.ApiKeys;
using Xunit;

namespace AutomateX.Tests;

// v4.11: a per-key client-IP allowlist (bare IPs + CIDRs). Empty = unrestricted; malformed entries are
// surfaced by FindInvalid so mint can reject them (a dropped entry could leave the list open).
public sealed class ClientIpAllowlistTests
{
    [Fact]
    public void Empty_allowlist_allows_everything()
    {
        Assert.True(default(ClientIpAllowlist).IsEmpty);
        Assert.True(default(ClientIpAllowlist).Allows(IPAddress.Parse("203.0.113.7")));
        Assert.True(ClientIpAllowlist.Parse([]).Allows(IPAddress.Parse("::1")));
    }

    [Fact]
    public void Bare_ip_allows_only_that_address()
    {
        var allow = ClientIpAllowlist.Parse(["198.51.100.7"]);
        Assert.True(allow.Allows(IPAddress.Parse("198.51.100.7")));
        Assert.False(allow.Allows(IPAddress.Parse("198.51.100.8")));
    }

    [Fact]
    public void Cidr_allows_members_and_rejects_outsiders()
    {
        var allow = ClientIpAllowlist.Parse(["203.0.113.0/24"]);
        Assert.True(allow.Allows(IPAddress.Parse("203.0.113.1")));
        Assert.True(allow.Allows(IPAddress.Parse("203.0.113.254")));
        Assert.False(allow.Allows(IPAddress.Parse("203.0.114.1")));
    }

    [Fact]
    public void Ipv4_mapped_ipv6_client_matches_an_ipv4_rule()
    {
        var allow = ClientIpAllowlist.Parse(["10.0.0.0/8"]);
        Assert.True(allow.Allows(IPAddress.Parse("::ffff:10.1.2.3")));
        Assert.False(allow.Allows(IPAddress.Parse("::ffff:11.1.2.3")));
    }

    [Fact]
    public void FindInvalid_flags_only_the_malformed_entries()
    {
        var invalid = ClientIpAllowlist.FindInvalid(["203.0.113.0/24", "not-an-ip", "198.51.100.7", "999.0.0.1"]);
        Assert.Equal(["not-an-ip", "999.0.0.1"], invalid);
    }

    [Fact]
    public void Parse_ignores_entries_FindInvalid_would_reject()
    {
        // Stored entries are validated at mint, but Parse stays defensive.
        var allow = ClientIpAllowlist.Parse(["garbage", "203.0.113.0/24"]);
        Assert.True(allow.Allows(IPAddress.Parse("203.0.113.9")));
    }
}
