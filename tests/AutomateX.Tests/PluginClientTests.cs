using System.Collections.Concurrent;
using AutomateX.Engine.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace AutomateX.Tests;

// v4.0: the engine-side PluginClient drives a real PluginHost child — describe, execute, and a trigger
// whose fire callbacks come back through the client to host-supplied callbacks. Skips if not built.
public sealed class PluginClientTests(ITestOutputHelper output)
{
    private sealed class RecordingCallbacks : IPluginHostCallbacks
    {
        public ConcurrentQueue<string?> Fires { get; } = new();

        private readonly ConcurrentDictionary<string, string> _state = new();

        public void OnLog(string? source, string level, string message) { }

        public Task OnFireAsync(string triggerId, string? payloadJson)
        {
            Fires.Enqueue(payloadJson);
            return Task.CompletedTask;
        }

        public Task<string?> StateGetAsync(string triggerId, string key) =>
            Task.FromResult(_state.GetValueOrDefault(key));

        public Task StateSetAsync(string triggerId, string key, string value, double? ttlSeconds)
        {
            _state[key] = value;
            return Task.CompletedTask;
        }

        public Task<bool> StateSetIfAbsentAsync(string triggerId, string key, string value, double? ttlSeconds) =>
            Task.FromResult(_state.TryAdd(key, value));

        public Task<bool> StateRemoveAsync(string triggerId, string key) =>
            Task.FromResult(_state.TryRemove(key, out _));
    }

    [Fact]
    public async Task Describes_and_executes_and_runs_a_trigger()
    {
        var (hostDll, pluginDll) = Locate();
        if (!OutOfProcGate.Ready(hostDll, pluginDll, output))
        {
            return;
        }

        var callbacks = new RecordingCallbacks();
        await using var client = new PluginClient(hostDll, pluginDll, callbacks, NullLogger.Instance);

        var describe = await client.DescribeAsync();
        Assert.Contains((System.Text.Json.Nodes.JsonArray)describe["result"]!["actions"]!,
            a => (string?)a!["type"] == "sample.echo");

        var result = await client.ExecuteActionAsync("sample.echo", """{"message":"via client"}""");
        Assert.Contains("via client", result);

        var triggerId = Guid.CreateVersion7().ToString();
        client.RunTrigger(triggerId, "sample.ticker", """{"intervalMilliseconds":30,"maxFires":2}""");

        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (callbacks.Fires.Count < 2 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        client.CancelTrigger(triggerId);
        Assert.Equal(2, callbacks.Fires.Count);
        Assert.All(callbacks.Fires, payload => Assert.Contains("\"tick\"", payload));
    }

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
