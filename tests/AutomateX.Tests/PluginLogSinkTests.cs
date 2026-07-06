using System.Collections.Concurrent;
using AutomateX.Engine.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace AutomateX.Tests;

// v4.9 m1: the supervisor fans every appended plugin log line out to an IPluginLogSink, keyed by dll
// path, without touching the ring the poll endpoint reads. The seam is tested directly (fast, no
// process) plus an out-of-proc pass that proves a real line both publishes and stays in the ring.
public sealed class PluginLogSinkTests(EngineFixture fixture, ITestOutputHelper output)
    : IClassFixture<EngineFixture>
{
    private sealed class FakeSink : IPluginLogSink
    {
        public ConcurrentQueue<(string Dll, PluginLogLine Line)> Published { get; } = new();

        public void Publish(string pluginDll, PluginLogLine line) => Published.Enqueue((pluginDll, line));
    }

    [Fact]
    public void OnLog_publishes_the_dll_and_line_to_the_sink()
    {
        var sink = new FakeSink();
        var supervisor = Supervisor(sink);
        var line = new PluginLogLine(42, DateTimeOffset.UtcNow, "Warning", "call-7", "hello");

        supervisor.OnLog("/plugins/acme.dll", line);

        Assert.True(sink.Published.TryDequeue(out var published));
        Assert.Equal("/plugins/acme.dll", published.Dll);
        Assert.Same(line, published.Line);
        Assert.Equal(42, published.Line.Seq);
        Assert.Empty(sink.Published);
    }

    [Fact]
    public void NullPluginLogSink_publish_is_a_safe_no_op()
    {
        var line = new PluginLogLine(1, DateTimeOffset.UtcNow, "Information", null, "x");
        var exception = Record.Exception(() => NullPluginLogSink.Instance.Publish("/plugins/acme.dll", line));
        Assert.Null(exception);
    }

    // A real plugin log line (sample.echo logs its message) must reach the sink and remain readable via
    // LogsSince — the stream is additive, the ring is untouched, and both see the same Seq.
    [Fact]
    public async Task Live_log_publishes_to_the_sink_and_leaves_the_ring_intact()
    {
        var (hostDll, pluginDll) = Locate();
        if (!OutOfProcGate.Ready(hostDll, pluginDll, output))
        {
            return;
        }

        var sink = new FakeSink();
        await using var supervisor = new PluginProcessSupervisor(
            fixture.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLoggerFactory.Instance, hostDll, logSink: sink);

        await supervisor.ExecuteActionAsync(pluginDll, "sample.echo", """{"message":"streamed"}""");

        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (sink.Published.IsEmpty && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.True(sink.Published.TryPeek(out var first), "no plugin log line reached the sink");
        Assert.Equal(pluginDll, first.Dll);
        Assert.Contains("streamed", first.Line.Message);

        // Ring unaffected: the same line is still served by the poll path with a matching Seq.
        var fromRing = supervisor.LogsSince(pluginDll, first.Line.Seq - 1);
        Assert.Contains(fromRing, x => x.Seq == first.Line.Seq && x.Message == first.Line.Message);
    }

    private PluginProcessSupervisor Supervisor(IPluginLogSink sink) =>
        new(fixture.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLoggerFactory.Instance, "unused-host.dll", logSink: sink);

    private static (string? HostDll, string? PluginDll) Locate()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AutomateX.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            return (null, null);
        }

        var config = AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        const string tfm = "net10.0";

        var host = Path.Combine(dir.FullName, "src", "AutomateX.PluginHost", "bin", config, tfm, "AutomateX.PluginHost.dll");
        var plugin = Path.Combine(dir.FullName, "samples", "AutomateX.SamplePlugin", "bin", config, tfm, "AutomateX.SamplePlugin.dll");
        return File.Exists(host) && File.Exists(plugin) ? (host, plugin) : (null, null);
    }
}
