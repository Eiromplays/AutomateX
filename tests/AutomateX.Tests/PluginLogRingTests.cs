using AutomateX.Engine.Plugins;
using Xunit;

namespace AutomateX.Tests;

public sealed class PluginLogRingTests
{
    [Fact]
    public void Since_returns_only_newer_lines()
    {
        var ring = new PluginLogRing();
        var first = ring.Add("Information", null, "one");
        var second = ring.Add("Error", "trigger", "two");

        Assert.Equal(2, ring.Since(0).Count);
        Assert.Equal([second.Seq], ring.Since(first.Seq).Select(x => x.Seq));
        Assert.Empty(ring.Since(second.Seq));
    }

    [Fact]
    public void Caps_at_capacity_dropping_oldest()
    {
        var ring = new PluginLogRing();
        for (var i = 0; i < 600; i++)
        {
            ring.Add("Information", null, $"line {i}");
        }

        var all = ring.Since(0);

        Assert.Equal(500, all.Count);
        Assert.Equal("line 599", all[^1].Message);
        Assert.Equal(101, all[0].Seq); // seqs 1..600, oldest 100 dropped
    }
}
