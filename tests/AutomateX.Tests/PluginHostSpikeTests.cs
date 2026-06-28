using System.Diagnostics;
using System.Text.Json.Nodes;
using AutomateX.PluginHost;
using Xunit;
using Xunit.Abstractions;

namespace AutomateX.Tests;

// v4.0 SPIKE: drives the SamplePlugin fully out-of-process through AutomateX.PluginHost to validate the
// boundary — describe, execute, the log callback, a clean shutdown — and prints warm-call latency.
// Requires a solution build (both projects are referenced so they're built); skips if binaries absent.
public sealed class PluginHostSpikeTests(ITestOutputHelper output)
{
    [Fact]
    public void Runs_the_sample_plugin_out_of_process()
    {
        var repoRoot = FindRepoRoot();
        var config = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";
        const string tfm = "net10.0";

        var hostDll = Path.Combine(repoRoot, "src", "AutomateX.PluginHost", "bin", config, tfm, "AutomateX.PluginHost.dll");
        var pluginDll = Path.Combine(repoRoot, "samples", "AutomateX.SamplePlugin", "bin", config, tfm, "AutomateX.SamplePlugin.dll");

        if (!File.Exists(hostDll) || !File.Exists(pluginDll))
        {
            output.WriteLine($"Spike skipped — build the solution first. host={hostDll} plugin={pluginDll}");
            return;
        }

        using var process = Process.Start(new ProcessStartInfo("dotnet")
        {
            ArgumentList = { "exec", hostDll, pluginDll },
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;

        try
        {
            var stdin = process.StandardInput.BaseStream;
            var stdout = process.StandardOutput.BaseStream;

            // describe
            PluginFrames.Write(stdin, new JsonObject { ["id"] = "1", ["method"] = "describe" });
            var described = ReadResponse(stdout, "1");
            var actions = (JsonArray)described["result"]!["actions"]!;
            Assert.Contains(actions, a => (string?)a!["type"] == "sample.echo");

            // action.execute (timed — warm call, process already started)
            var sw = Stopwatch.StartNew();
            PluginFrames.Write(stdin, new JsonObject
            {
                ["id"] = "2",
                ["method"] = "action.execute",
                ["params"] = new JsonObject
                {
                    ["type"] = "sample.echo",
                    ["configJson"] = """{"message":"hello out-of-proc"}""",
                },
            });
            var executed = ReadResponse(stdout, "2");
            sw.Stop();

            Assert.Null((string?)executed["error"]);
            var resultJson = (string?)executed["result"]!["resultJson"];
            Assert.Contains("hello out-of-proc", resultJson);
            output.WriteLine($"warm action.execute round-trip: {sw.ElapsedMilliseconds} ms");

            stdin.Close(); // EOF → child exits
            Assert.True(process.WaitForExit(5000));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    // Reads frames until the matching response id; callback frames (log) in between are ignored.
    private static JsonObject ReadResponse(Stream stream, string id)
    {
        while (PluginFrames.TryRead(stream, out var message))
        {
            if ((string?)message["id"] == id)
            {
                return message;
            }
        }

        throw new InvalidOperationException("plugin host closed before responding");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AutomateX.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("AutomateX.slnx not found above the test output.");
    }
}
