using AutomateX.Modules.Connections;
using Xunit;

namespace AutomateX.Tests;

// Rules encoded ahead of the implementation (recorded decision, v1.2):
// provided keys overwrite, null values delete, absent keys stay untouched.
// An empty merge result is legal here — rejecting it is the endpoint's rule.
public sealed class ConnectionSecretsMergerTests
{
    private static readonly Dictionary<string, string> Existing = new()
    {
        ["token"] = "old-token",
        ["url"] = "https://api.example.com",
    };

    [Fact]
    public void Provided_keys_overwrite()
    {
        var merged = ConnectionSecretsMerger.Merge(Existing, new Dictionary<string, string?>() { ["token"] = "new-token" });

        Assert.Equal("new-token", merged["token"]);
        Assert.Equal("https://api.example.com", merged["url"]);
    }

    [Fact]
    public void Null_values_delete()
    {
        var merged = ConnectionSecretsMerger.Merge(Existing, new Dictionary<string, string?>() { ["url"] = null });

        Assert.Equal(["token"], merged.Keys);
    }

    [Fact]
    public void Absent_keys_are_untouched_and_new_keys_added()
    {
        var merged = ConnectionSecretsMerger.Merge(Existing, new Dictionary<string, string?>() { ["extra"] = "value" });

        Assert.Equal(3, merged.Count);
        Assert.Equal("old-token", merged["token"]);
        Assert.Equal("value", merged["extra"]);
    }

    [Fact]
    public void Deleting_everything_yields_empty()
    {
        var merged = ConnectionSecretsMerger.Merge(Existing, new Dictionary<string, string?>() { ["token"] = null, ["url"] = null });

        Assert.Empty(merged);
    }
}
