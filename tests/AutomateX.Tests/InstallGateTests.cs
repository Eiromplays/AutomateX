using AutomateX.Engine.Plugins;
using Xunit;

namespace AutomateX.Tests;

public sealed class InstallGateTests
{
    [Fact]
    public void Verified_without_permissions_installs_at_the_base_bar() =>
        Assert.Equal(
            InstallDecision.Allowed,
            InstallGate.Evaluate(CatalogTrust.Verified, isInstanceAdmin: false, acknowledged: false, declaresPermissions: false));

    [Fact]
    public void Declared_permissions_need_acknowledgement_even_when_verified()
    {
        Assert.Equal(
            InstallDecision.NeedsAcknowledgement,
            InstallGate.Evaluate(CatalogTrust.Verified, isInstanceAdmin: false, acknowledged: false, declaresPermissions: true));
        Assert.Equal(
            InstallDecision.Allowed,
            InstallGate.Evaluate(CatalogTrust.Verified, isInstanceAdmin: false, acknowledged: true, declaresPermissions: true));
    }

    [Fact]
    public void Untrusted_requires_an_instance_admin()
    {
        Assert.Equal(
            InstallDecision.RequiresInstanceAdmin,
            InstallGate.Evaluate(CatalogTrust.Untrusted, isInstanceAdmin: false, acknowledged: true, declaresPermissions: false));
        Assert.Equal(
            InstallDecision.NeedsAcknowledgement,
            InstallGate.Evaluate(CatalogTrust.Untrusted, isInstanceAdmin: true, acknowledged: false, declaresPermissions: false));
        Assert.Equal(
            InstallDecision.Allowed,
            InstallGate.Evaluate(CatalogTrust.Untrusted, isInstanceAdmin: true, acknowledged: true, declaresPermissions: false));
    }

    [Fact]
    public void Blocked_is_refused() =>
        Assert.Equal(
            InstallDecision.Refused,
            InstallGate.Evaluate(CatalogTrust.Blocked, isInstanceAdmin: true, acknowledged: true, declaresPermissions: false));
}

public sealed class PluginCompatibilityTests
{
    [Theory]
    [InlineData("5.2.0", "5.0.0", true)]
    [InlineData("5.0.0", "5.0.0", true)]
    [InlineData("5.0.0", "5.2.0", false)]
    [InlineData("5.0.0", null, true)]
    [InlineData(null, "5.0.0", true)]
    public void IsSupported_checks_the_minimum(string? appVersion, string? minimum, bool expected) =>
        Assert.Equal(expected, PluginCompatibility.IsSupported(appVersion, minimum));
}
