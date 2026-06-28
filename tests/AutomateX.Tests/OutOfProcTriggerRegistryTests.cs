using AutomateX.Engine;
using AutomateX.Engine.Plugins;
using AutomateX.Engine.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace AutomateX.Tests;

// v4.0: with OutOfProcPlugins on, TriggerRegistry discovers a plugin's trigger types by describing its
// host (no in-host load) and records which plugin dll backs each, so the host can run it out-of-proc.
public sealed class OutOfProcTriggerRegistryTests(EngineFixture fixture, ITestOutputHelper output)
    : IClassFixture<EngineFixture>
{
    [Fact]
    public async Task Discovers_plugin_trigger_types_out_of_process()
    {
        var (hostDll, sampleBin) = Locate();
        if (hostDll is null || sampleBin is null)
        {
            output.WriteLine("Skipped — build the solution first.");
            return;
        }

        var pluginsRoot = Path.Combine(Path.GetTempPath(), $"ax-oop-trig-{Guid.CreateVersion7():N}");
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
            var registry = new TriggerRegistry(
                [], plugins, supervisor, options, fixture.Host.Services, NullLogger<TriggerRegistry>.Instance);

            Assert.True(registry.Contains("sample.ticker"));
            Assert.Contains(registry.Descriptors, d => d.Type == "sample.ticker");
            Assert.EndsWith("AutomateX.SamplePlugin.dll", registry.OutOfProcPluginDll("sample.ticker"));
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
