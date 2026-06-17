using System.Net;
using AutomateX.Engine.Actions;
using Xunit;

namespace AutomateX.Tests;

public sealed class SsrfGuardTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]      // loopback
    [InlineData("::1", true)]            // loopback v6
    [InlineData("0.0.0.0", true)]        // unspecified
    [InlineData("10.1.2.3", true)]       // RFC1918
    [InlineData("172.16.0.1", true)]     // RFC1918 lower bound
    [InlineData("172.31.255.255", true)] // RFC1918 upper bound
    [InlineData("172.32.0.1", false)]    // just outside 172.16/12
    [InlineData("192.168.1.1", true)]    // RFC1918
    [InlineData("169.254.169.254", true)] // link-local / cloud metadata
    [InlineData("fc00::1", true)]        // unique local
    [InlineData("fe80::1", true)]        // link-local v6
    [InlineData("8.8.8.8", false)]       // public
    [InlineData("1.1.1.1", false)]       // public
    [InlineData("2606:4700:4700::1111", false)] // public v6
    public void IsBlockedAddress_classifies(string ip, bool blocked) =>
        Assert.Equal(blocked, SsrfGuard.IsBlockedAddress(IPAddress.Parse(ip)));
}
