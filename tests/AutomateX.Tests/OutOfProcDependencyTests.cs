using AutomateX.Engine.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace AutomateX.Tests;

// v4.0: proves the PluginHost's AssemblyLoadContext resolves a plugin's bundled NuGet dependencies. The
// sample plugin's sample.humanize action pulls in Humanizer (a plugin-private dependency the host doesn't
// reference), so this is the only test that exercises transitive dependency loading out-of-process.
public sealed class OutOfProcDependencyTests(EngineFixture fixture, ITestOutputHelper output)
    : IClassFixture<EngineFixture>
{
    [Fact]
    public async Task Plugin_with_nuget_dependencies_loads_and_runs_out_of_process()
    {
        var (hostDll, sampleDll) = Locate();
        if (!OutOfProcGate.Ready(hostDll, sampleDll, output))
        {
            return;
        }

        var supervisor = new PluginProcessSupervisor(
            fixture.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            fixture.Host.Services.GetRequiredService<ILoggerFactory>(),
            hostDll!);

        await using (supervisor)
        {
            // sample.humanize calls Humanizer's string.Humanize() — forcing the plugin-private dependency to
            // load in the host ALC. Had Humanizer not resolved, this would be an assembly-load error.
            const string config = """{"text":"hello_world_test"}""";

            var result = await supervisor.ExecuteActionAsync(sampleDll!, "sample.humanize", config);

            Assert.NotNull(result);
            Assert.DoesNotContain("Could not load file or assembly", result!, StringComparison.OrdinalIgnoreCase);
            // The underscored input is transformed into spaced words — i.e. Humanizer loaded and ran.
            Assert.DoesNotContain("hello_world_test", result!);
            Assert.Contains("world test", result!, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static (string? HostDll, string? SampleDll) Locate()
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
        var sample = Path.Combine(
            dir.FullName, "samples", "AutomateX.SamplePlugin", "bin", config, tfm, "AutomateX.SamplePlugin.dll");
        return File.Exists(host) && File.Exists(sample) ? (host, sample) : (null, null);
    }
}
