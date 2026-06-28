using System.Diagnostics;
using System.Text.Json.Nodes;
using AutomateX.Plugin.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace AutomateX.Tests;

// v4.0: drives the SamplePlugin fully out-of-process through AutomateX.PluginHost to validate the
// boundary — describe, execute (+ log callback), and the trigger.run channel (fire callbacks + cancel).
// Requires a solution build (both projects are referenced so they're built); skips if binaries absent.
public sealed class PluginHostSpikeTests(ITestOutputHelper output)
{
    [Fact]
    public void Runs_actions_out_of_process()
    {
        using var host = StartHostProcess();
        if (host is null)
        {
            return; // binaries not built — see StartHostProcess
        }

        var stdin = host.StandardInput.BaseStream;
        var stdout = host.StandardOutput.BaseStream;

        PluginFrames.Write(stdin, new JsonObject { ["id"] = "1", ["method"] = "describe" });
        var result = ReadResponse(stdout, "1")["result"]!;
        var echo = Assert.Single((JsonArray)result["actions"]!, a => (string?)a!["type"] == "sample.echo")!;
        Assert.NotNull(echo["configSchema"]); // schema generated plugin-side, out-of-proc
        Assert.Contains((JsonArray)result["triggers"]!, t => (string?)t!["type"] == "sample.ticker");

        var sw = Stopwatch.StartNew();
        PluginFrames.Write(stdin, new JsonObject
        {
            ["id"] = "2",
            ["method"] = "action.execute",
            ["params"] = new JsonObject { ["type"] = "sample.echo", ["configJson"] = """{"message":"hello out-of-proc"}""" },
        });
        var executed = ReadResponse(stdout, "2");
        sw.Stop();

        Assert.Null((string?)executed["error"]);
        Assert.Contains("hello out-of-proc", (string?)executed["result"]!["resultJson"]);
        output.WriteLine($"warm action.execute round-trip: {sw.ElapsedMilliseconds} ms");

        stdin.Close();
        Assert.True(host.WaitForExit(5000));
    }

    [Fact]
    public void Runs_a_trigger_with_fire_callbacks_then_cancels()
    {
        using var host = StartHostProcess();
        if (host is null)
        {
            return;
        }

        var stdin = host.StandardInput.BaseStream;
        var stdout = host.StandardOutput.BaseStream;
        var triggerId = Guid.CreateVersion7().ToString();

        PluginFrames.Write(stdin, new JsonObject
        {
            ["id"] = "run",
            ["method"] = "trigger.run",
            ["params"] = new JsonObject
            {
                ["triggerId"] = triggerId,
                ["type"] = "sample.ticker",
                ["configJson"] = """{"intervalMilliseconds":30,"maxFires":2}""",
            },
        });

        // The ticker fires twice; each fire is a child→host call we must ack so the loop continues.
        var fires = 0;
        while (fires < 2 && PluginFrames.TryRead(stdout, out var frame))
        {
            if ((string?)frame["method"] == "trigger.fire")
            {
                fires++;
                Assert.Contains("\"tick\"", (string?)frame["params"]!["payloadJson"]);
                PluginFrames.Write(stdin, new JsonObject { ["id"] = (string?)frame["id"], ["result"] = new JsonObject() });
            }
        }

        Assert.Equal(2, fires);

        PluginFrames.Write(stdin, new JsonObject { ["method"] = "cancel", ["params"] = new JsonObject { ["triggerId"] = triggerId } });
        stdin.Close();
        Assert.True(host.WaitForExit(5000));
    }

    private Process? StartHostProcess()
    {
        var repoRoot = FindRepoRoot();
        var config = AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        const string tfm = "net10.0";

        var hostDll = Path.Combine(repoRoot, "src", "AutomateX.PluginHost", "bin", config, tfm, "AutomateX.PluginHost.dll");
        var pluginDll = Path.Combine(repoRoot, "samples", "AutomateX.SamplePlugin", "bin", config, tfm, "AutomateX.SamplePlugin.dll");

        if (!File.Exists(hostDll) || !File.Exists(pluginDll))
        {
            output.WriteLine($"Skipped — build the solution first. host={hostDll} plugin={pluginDll}");
            return null;
        }

        return Process.Start(new ProcessStartInfo("dotnet")
        {
            ArgumentList = { "exec", hostDll, pluginDll },
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        });
    }

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
