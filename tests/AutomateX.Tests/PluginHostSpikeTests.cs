using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
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

    // v4.7: out-of-proc plugin HTTP obeys the egress guard. Same loopback URL, same server — only the
    // AUTOMATEX_EGRESS_BLOCK_PRIVATE env differs — so a 200 with the guard off and an error with it on
    // proves it's the guard doing the blocking, not an unreachable target. v4.8: with the guard on *and*
    // the loopback host on AUTOMATEX_EGRESS_ALLOWLIST, the child reaches it again — the allowlist widens.
    [Fact]
    public void Plugin_http_egress_guard_blocks_loopback_only_when_on()
    {
        using var listener = new HttpListener();
        var url = $"http://127.0.0.1:{FreeTcpPort()}/";
        listener.Prefixes.Add(url);
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            output.WriteLine($"Skipped — cannot bind a loopback listener here: {ex.Message}");
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                while (listener.IsListening)
                {
                    var context = await listener.GetContextAsync();
                    context.Response.StatusCode = 200;
                    context.Response.Close();
                }
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                // listener disposed at test end
            }
        });

        // Guard off: the child reaches the loopback server.
        var off = RunHttpGet(blockPrivateEgress: false, url);
        if (off is null)
        {
            return; // OOP binaries not built — StartHostProcess already handled the skip
        }

        Assert.Null((string?)off["error"]);
        Assert.Contains("\"statusCode\":200", (string?)off["result"]!["resultJson"]);

        // Guard on: EgressGuard refuses the loopback address before connecting, so the action errors.
        var on = RunHttpGet(blockPrivateEgress: true, url)!;
        output.WriteLine($"guarded error: {(string?)on["error"]}");
        Assert.NotNull((string?)on["error"]);

        // Guard on but the loopback host is allowlisted: the widened policy lets the child through again.
        var allowlisted = RunHttpGet(blockPrivateEgress: true, url, allowlist: "127.0.0.1")!;
        output.WriteLine($"allowlisted result: {(string?)allowlisted["result"]?["resultJson"]}");
        Assert.Null((string?)allowlisted["error"]);
        Assert.Contains("\"statusCode\":200", (string?)allowlisted["result"]!["resultJson"]);
    }

    private Process? StartHostProcess(bool blockPrivateEgress = false, string? allowlist = null)
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
            Assert.False(
                Environment.GetEnvironmentVariable("AUTOMATEX_REQUIRE_OOP") == "1",
                "AUTOMATEX_REQUIRE_OOP is set but the PluginHost or plugin binaries were not found — build the solution.");
            output.WriteLine($"Skipped — build the solution first. host={hostDll} plugin={pluginDll}");
            return null;
        }

        var startInfo = new ProcessStartInfo("dotnet")
        {
            ArgumentList = { "exec", hostDll, pluginDll },
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        if (blockPrivateEgress)
        {
            startInfo.Environment["AUTOMATEX_EGRESS_BLOCK_PRIVATE"] = "true";
        }

        if (allowlist is not null)
        {
            startInfo.Environment["AUTOMATEX_EGRESS_ALLOWLIST"] = allowlist;
        }

        return Process.Start(startInfo);
    }

    // Runs one sample.httpget out-of-process against url and returns the host's response frame (result
    // or error), or null if the OOP binaries aren't built. Each call is its own host process, so the
    // egress env is fixed per run.
    private JsonObject? RunHttpGet(bool blockPrivateEgress, string url, string? allowlist = null)
    {
        using var host = StartHostProcess(blockPrivateEgress, allowlist);
        if (host is null)
        {
            return null;
        }

        var stdin = host.StandardInput.BaseStream;
        var stdout = host.StandardOutput.BaseStream;

        PluginFrames.Write(stdin, new JsonObject
        {
            ["id"] = "g",
            ["method"] = "action.execute",
            ["params"] = new JsonObject
            {
                ["type"] = "sample.httpget",
                ["configJson"] = new JsonObject { ["url"] = url }.ToJsonString(),
            },
        });
        var response = ReadResponse(stdout, "g");
        stdin.Close();
        host.WaitForExit(5000);
        return response;
    }

    private static int FreeTcpPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
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
