using AutomateX.Engine;
using AutomateX.Engine.Connections;
using AutomateX.Engine.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace AutomateX.Tests;

// v4.0: with OutOfProcPlugins on, ConnectionTypeRegistry discovers a plugin's connection types by
// describing its host (no in-host instance) and routes BuildOAuthConfig/Test to the supervisor.
public sealed class OutOfProcConnectionTypeRegistryTests(EngineFixture fixture, ITestOutputHelper output)
    : IClassFixture<EngineFixture>
{
    [Fact]
    public async Task Discovers_and_routes_plugin_connection_types_out_of_process()
    {
        var (hostDll, sampleBin) = Locate();
        if (!OutOfProcGate.Ready(hostDll, sampleBin, output))
        {
            return;
        }

        var pluginsRoot = Path.Combine(Path.GetTempPath(), $"ax-oop-conn-{Guid.CreateVersion7():N}");
        var pluginDir = Path.Combine(pluginsRoot, "AutomateX.SamplePlugin");
        Directory.CreateDirectory(pluginDir);
        foreach (var file in Directory.EnumerateFiles(sampleBin))
        {
            File.Copy(file, Path.Combine(pluginDir, Path.GetFileName(file)));
        }

        var options = Options.Create(new EngineOptions { OutOfProcPlugins = true, PluginsPath = pluginsRoot });
        var plugins = new PluginAssemblies(options, NullLogger<PluginAssemblies>.Instance);
        var supervisor = new PluginProcessSupervisor(
            fixture.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            fixture.Host.Services.GetRequiredService<ILoggerFactory>(),
            hostDll);

        try
        {
            var registry = new ConnectionTypeRegistry(
                [], plugins, supervisor, NullLogger<ConnectionTypeRegistry>.Instance);

            var descriptor = Assert.Single(registry.Descriptors, d => d.Type == "sample.conn");
            Assert.True(descriptor.IsOAuth);
            Assert.True(descriptor.IsTester);
            Assert.Contains(descriptor.Fields, f => f.Key == "clientId");
            Assert.EndsWith("AutomateX.SamplePlugin.dll", registry.OutOfProcPluginDll("sample.conn"));
            Assert.True(registry.IsOAuth("sample.conn"));
            Assert.True(registry.HasTester("sample.conn"));

            var config = await registry.BuildOAuthConfigAsync(
                "sample.conn", new Dictionary<string, string> { ["clientId"] = "abc", ["clientSecret"] = "shh" });
            Assert.NotNull(config);
            Assert.Equal("abc", config.ClientId);
            Assert.Equal("https://auth.example.com/authorize", config.AuthorizationEndpoint);
            Assert.True(config.UsePkce);

            using var http = new HttpClient();
            var ok = await registry.TestAsync("sample.conn", new Dictionary<string, string> { ["clientId"] = "abc" }, http);
            Assert.NotNull(ok);
            Assert.True(ok.Ok);

            var missing = await registry.TestAsync("sample.conn", new Dictionary<string, string>(), http);
            Assert.NotNull(missing);
            Assert.False(missing.Ok);
        }
        finally
        {
            await supervisor.DisposeAsync();
            Directory.Delete(pluginsRoot, recursive: true);
        }
    }

    private static (string? HostDll, string? SampleBin) Locate()
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
        var sampleBin = Path.Combine(dir.FullName, "samples", "AutomateX.SamplePlugin", "bin", config, tfm);
        return File.Exists(host) && Directory.Exists(sampleBin) ? (host, sampleBin) : (null, null);
    }
}
