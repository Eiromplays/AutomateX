using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;
using AutomateX.Plugin.Protocol;
using Microsoft.Extensions.Logging;

namespace AutomateX.Engine.Plugins;

// The host's callbacks for a running plugin process: the child invokes these over the pipe.
public interface IPluginHostCallbacks
{
    void OnLog(string? source, string level, string message);

    Task OnFireAsync(string triggerId, string? payloadJson);

    Task<string?> StateGetAsync(string triggerId, string key);

    Task StateSetAsync(string triggerId, string key, string value, double? ttlSeconds);

    Task<bool> StateSetIfAbsentAsync(string triggerId, string key, string value, double? ttlSeconds);

    Task<bool> StateRemoveAsync(string triggerId, string key);
}

// Engine-side connection to one AutomateX.PluginHost child process. Full-duplex over the child's
// stdio: this side issues requests (describe/action.execute/trigger.run/connection.*/cancel) and
// answers the child's callbacks (log/trigger.fire/trigger.state.*). One warm process per plugin; the
// supervisor owns its lifecycle.
public sealed class PluginClient : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Stream _stdin;
    private readonly Stream _stdout;
    private readonly IPluginHostCallbacks _callbacks;
    private readonly ILogger _logger;
    private readonly object _writeLock = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonObject>> _pending = new();
    private readonly Task _readLoop;
    private int _callCounter;

    public PluginClient(string hostDll, string pluginDll, IPluginHostCallbacks callbacks, ILogger logger)
    {
        _callbacks = callbacks;
        _logger = logger;
        _process = Process.Start(new ProcessStartInfo("dotnet")
        {
            ArgumentList = { "exec", hostDll, pluginDll },
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException($"Failed to start plugin host for {pluginDll}.");

        _stdin = _process.StandardInput.BaseStream;
        _stdout = _process.StandardOutput.BaseStream;
        _readLoop = Task.Run(ReadLoopAsync);
    }

    public bool HasExited => _process.HasExited;

    // Per-plugin observability: the captured log tail + live process facts for the ops surface.
    public PluginLogRing Logs { get; } = new();

    public int Pid => _process.Id;

    public DateTimeOffset StartedAt
    {
        get
        {
            try
            {
                return new DateTimeOffset(_process.StartTime.ToUniversalTime(), TimeSpan.Zero);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return DateTimeOffset.MinValue;
            }
        }
    }

    public long MemoryBytes
    {
        get
        {
            try
            {
                _process.Refresh();
                return _process.WorkingSet64;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return 0;
            }
        }
    }

    public async Task<JsonObject> DescribeAsync(CancellationToken cancellationToken = default) =>
        await CallAsync("describe", new JsonObject(), cancellationToken);

    public async Task<string?> ExecuteActionAsync(string type, string configJson, CancellationToken cancellationToken = default)
    {
        var reply = await CallAsync(
            "action.execute", new JsonObject { ["type"] = type, ["configJson"] = configJson }, cancellationToken);
        return (string?)reply["result"]?["resultJson"];
    }

    public async Task<JsonObject> BuildOAuthConfigAsync(string type, JsonObject values, CancellationToken cancellationToken = default) =>
        (JsonObject)(await CallAsync(
            "connection.buildOAuthConfig", new JsonObject { ["type"] = type, ["values"] = values }, cancellationToken))["result"]!;

    public async Task<JsonObject> TestConnectionAsync(string type, JsonObject values, CancellationToken cancellationToken = default) =>
        (JsonObject)(await CallAsync(
            "connection.test", new JsonObject { ["type"] = type, ["values"] = values }, cancellationToken))["result"]!;

    // Fire-and-forget: the listener runs until cancelled, calling back as it goes.
    public void RunTrigger(string triggerId, string type, string configJson) =>
        Send(new JsonObject
        {
            ["method"] = "trigger.run",
            ["params"] = new JsonObject { ["triggerId"] = triggerId, ["type"] = type, ["configJson"] = configJson },
        });

    public void CancelTrigger(string triggerId) =>
        Send(new JsonObject { ["method"] = "cancel", ["params"] = new JsonObject { ["triggerId"] = triggerId } });

    private async Task<JsonObject> CallAsync(string method, JsonObject parameters, CancellationToken cancellationToken)
    {
        var id = $"h{Interlocked.Increment(ref _callCounter)}";
        var completion = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        Send(new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters });

        await using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        var reply = await completion.Task;
        return (string?)reply["error"] is { } error
            ? throw new PluginHostException(error)
            : reply;
    }

    private void Send(JsonObject message)
    {
        lock (_writeLock)
        {
            PluginFrames.Write(_stdin, message);
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (PluginFrames.TryRead(_stdout, out var frame))
            {
                var method = (string?)frame["method"];
                var id = (string?)frame["id"];

                if (method is null)
                {
                    if (id is not null && _pending.TryRemove(id, out var completion))
                    {
                        completion.TrySetResult(frame);
                    }

                    continue;
                }

                await HandleCallbackAsync(method, id, frame["params"] as JsonObject ?? []);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Plugin host read loop failed");
        }
        finally
        {
            // The process is gone — fail anything still waiting so callers don't hang.
            foreach (var completion in _pending.Values)
            {
                completion.TrySetException(new PluginHostException("Plugin host exited."));
            }

            _pending.Clear();
        }
    }

    private async Task HandleCallbackAsync(string method, string? id, JsonObject parameters)
    {
        try
        {
            switch (method)
            {
                case "log":
                {
                    var source = (string?)parameters["callId"];
                    var level = (string?)parameters["level"] ?? "Information";
                    var message = (string?)parameters["message"] ?? "";
                    Logs.Add(level, source, message);
                    _callbacks.OnLog(source, level, message);
                    return; // notification — no reply
                }

                case "trigger.error":
                {
                    var source = (string?)parameters["triggerId"];
                    var message = (string?)parameters["message"] ?? "";
                    Logs.Add("Error", source, message);
                    _callbacks.OnLog(source, "Error", message);
                    return;
                }

                case "trigger.fire":
                    await _callbacks.OnFireAsync((string)parameters["triggerId"]!, (string?)parameters["payloadJson"]);
                    Reply(id, []);
                    return;

                case "trigger.state.get":
                    Reply(id, new JsonObject { ["value"] = await _callbacks.StateGetAsync(Trigger(parameters), Key(parameters)) });
                    return;

                case "trigger.state.set":
                    await _callbacks.StateSetAsync(Trigger(parameters), Key(parameters), (string)parameters["value"]!, (double?)parameters["ttlSeconds"]);
                    Reply(id, []);
                    return;

                case "trigger.state.setIfAbsent":
                    Reply(id, new JsonObject
                    {
                        ["acquired"] = await _callbacks.StateSetIfAbsentAsync(
                            Trigger(parameters), Key(parameters), (string)parameters["value"]!, (double?)parameters["ttlSeconds"]),
                    });
                    return;

                case "trigger.state.remove":
                    Reply(id, new JsonObject { ["removed"] = await _callbacks.StateRemoveAsync(Trigger(parameters), Key(parameters)) });
                    return;

                default:
                    if (id is not null)
                    {
                        Send(new JsonObject { ["id"] = id, ["error"] = $"Unknown callback '{method}'." });
                    }

                    return;
            }
        }
        catch (Exception ex) when (id is not null)
        {
            Send(new JsonObject { ["id"] = id, ["error"] = ex.Message });
        }

        static string Trigger(JsonObject p) => (string)p["triggerId"]!;
        static string Key(JsonObject p) => (string)p["key"]!;
    }

    private void Reply(string? id, JsonObject result)
    {
        if (id is not null)
        {
            Send(new JsonObject { ["id"] = id, ["result"] = result });
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // already gone
        }

        await _readLoop.ConfigureAwait(false);
        _process.Dispose();
    }
}

public sealed class PluginHostException(string message) : Exception(message);
