using AutomateX.Engine;
using AutomateX.Engine.Plugins;
using AutomateX.Web;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutomateX.Tests;

// v4.9 m2: the SignalR sink resolves a plugin dll back to its global name (via PluginAssemblies, the map
// GetPluginStatus.ResolveDll uses in reverse) and broadcasts the line to that plugin's group. A dll it
// can't resolve is dropped — best-effort, the ring + poll remain the backstop.
public sealed class SignalRPluginLogSinkTests
{
    [Fact]
    public void Publish_broadcasts_the_line_to_the_resolved_plugin_group()
    {
        using var temp = new TempPlugins("Acme");
        var clients = new RecordingHubClients();
        var sink = new SignalRPluginLogSink(
            new FakeHubContext(clients), temp.Assemblies, NullLogger<SignalRPluginLogSink>.Instance);
        var line = new PluginLogLine(3, DateTimeOffset.UtcNow, "Information", "call-1", "hi");

        sink.Publish(temp.DllPath("Acme"), line);

        Assert.Equal("plugin:Acme", Assert.Single(clients.GroupNames));
        var (method, args) = Assert.Single(clients.Proxy.Sends);
        Assert.Equal("pluginLog", method);
        Assert.Same(line, Assert.Single(args));
    }

    [Fact]
    public void Publish_drops_a_line_for_an_unknown_dll()
    {
        using var temp = new TempPlugins("Acme");
        var clients = new RecordingHubClients();
        var sink = new SignalRPluginLogSink(
            new FakeHubContext(clients), temp.Assemblies, NullLogger<SignalRPluginLogSink>.Instance);

        sink.Publish(temp.DllPath("Ghost"), new PluginLogLine(1, DateTimeOffset.UtcNow, "Information", null, "x"));

        Assert.Empty(clients.GroupNames);
        Assert.Empty(clients.Proxy.Sends);
    }

    // A throwaway plugins root laid out by convention (<root>/<Name>/<Name>.dll) so PluginAssemblies can
    // enumerate it. Only the path shape matters — the dll is an empty file, never loaded.
    private sealed class TempPlugins : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"atx-plugins-{Guid.NewGuid():N}");

        public TempPlugins(string name)
        {
            Directory.CreateDirectory(Path.Combine(_root, name));
            File.WriteAllText(DllPath(name), "");
            Assemblies = new PluginAssemblies(
                Options.Create(new EngineOptions { PluginsPath = _root }), NullLogger<PluginAssemblies>.Instance);
        }

        public PluginAssemblies Assemblies { get; }

        public string DllPath(string name) => Path.Combine(_root, name, $"{name}.dll");

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }

    private sealed class FakeHubContext(IHubClients clients) : IHubContext<PluginLogsHub>
    {
        public IHubClients Clients { get; } = clients;

        public IGroupManager Groups { get; } = new NoopGroupManager();
    }

    private sealed class NoopGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingHubClients : IHubClients
    {
        public List<string> GroupNames { get; } = [];

        public RecordingClientProxy Proxy { get; } = new();

        public IClientProxy Group(string groupName)
        {
            GroupNames.Add(groupName);
            return Proxy;
        }

        public IClientProxy All => throw new NotSupportedException();

        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

        public IClientProxy Client(string connectionId) => throw new NotSupportedException();

        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();

        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();

        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) =>
            throw new NotSupportedException();

        public IClientProxy User(string userId) => throw new NotSupportedException();

        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class RecordingClientProxy : IClientProxy
    {
        public List<(string Method, object?[] Args)> Sends { get; } = [];

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Sends.Add((method, args));
            return Task.CompletedTask;
        }
    }
}
