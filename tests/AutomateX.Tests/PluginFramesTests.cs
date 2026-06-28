using System.Text.Json.Nodes;
using AutomateX.PluginHost;
using Xunit;

namespace AutomateX.Tests;

// The host↔plugin framing: a length-prefixed JSON message round-trips, and back-to-back frames on one
// stream read back in order (the multiplexing the supervisor relies on).
public sealed class PluginFramesTests
{
    [Fact]
    public void Round_trips_a_message()
    {
        using var stream = new MemoryStream();
        PluginFrames.Write(stream, new JsonObject { ["method"] = "describe", ["id"] = "1" });
        stream.Position = 0;

        Assert.True(PluginFrames.TryRead(stream, out var message));
        Assert.Equal("describe", (string?)message["method"]);
        Assert.Equal("1", (string?)message["id"]);
    }

    [Fact]
    public void Reads_consecutive_frames_in_order()
    {
        using var stream = new MemoryStream();
        PluginFrames.Write(stream, new JsonObject { ["id"] = "a" });
        PluginFrames.Write(stream, new JsonObject { ["id"] = "b" });
        stream.Position = 0;

        Assert.True(PluginFrames.TryRead(stream, out var first));
        Assert.True(PluginFrames.TryRead(stream, out var second));
        Assert.False(PluginFrames.TryRead(stream, out _)); // clean EOF
        Assert.Equal("a", (string?)first["id"]);
        Assert.Equal("b", (string?)second["id"]);
    }
}
