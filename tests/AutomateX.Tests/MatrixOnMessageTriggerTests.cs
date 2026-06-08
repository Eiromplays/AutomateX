using System.Net;
using System.Text;
using AutomateX.Engine.Triggers;
using AutomateX.Plugins.Matrix;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// The Jarvis ears: a sync long-poll listener. Rules pinned here:
// initial sync history is skipped, own messages are always ignored (loop
// protection), non-message events are ignored, the since-token advances,
// the optional room filter applies, and sync failures throw (the host's
// backoff restart handles them).
public sealed class MatrixOnMessageTriggerTests
{
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        private readonly Queue<string> _responses = new();

        public ScriptedHandler(params string[] responses)
        {
            foreach (var response in responses)
            {
                _responses.Enqueue(response);
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.ToString());

            if (_responses.Count == 0)
            {
                // Script exhausted: park until the test cancels.
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            var body = _responses.Dequeue();
            return body.StartsWith("ERROR", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent(body),
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
        }
    }

    private const string WhoAmI = """{"user_id":"@bot:hs"}""";

    private const string InitialSync =
        """
        {"next_batch":"t1","rooms":{"join":{"!r:hs":{"timeline":{"events":[
          {"type":"m.room.message","sender":"@alice:hs","event_id":"$old","origin_server_ts":1,
           "content":{"msgtype":"m.text","body":"old history message"}}
        ]}}}}}
        """;

    private const string SecondSync =
        """
        {"next_batch":"t2","rooms":{"join":{
          "!r:hs":{"timeline":{"events":[
            {"type":"m.room.message","sender":"@alice:hs","event_id":"$e1","origin_server_ts":123,
             "content":{"msgtype":"m.text","body":"hello jarvis"}},
            {"type":"m.room.message","sender":"@bot:hs","event_id":"$e2","origin_server_ts":124,
             "content":{"msgtype":"m.text","body":"my own reply"}},
            {"type":"m.reaction","sender":"@alice:hs","event_id":"$e3","origin_server_ts":125,
             "content":{}}
          ]}},
          "!other:hs":{"timeline":{"events":[
            {"type":"m.room.message","sender":"@carol:hs","event_id":"$e4","origin_server_ts":126,
             "content":{"msgtype":"m.text","body":"other room message"}}
          ]}}
        }}}
        """;

    private static async Task<List<string?>> RunAsync(ScriptedHandler handler, MatrixOnMessageConfig config, int stopAfterFires)
    {
        List<string?> fired = [];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var runner = new OnMessageTrigger();
        var context = new AutomateX.Plugin.Sdk.TriggerContext
        {
            Logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            Http = new HttpClient(handler),
            TriggerId = Guid.CreateVersion7(),
            WorkflowId = Guid.CreateVersion7(),
            Fire = payload =>
            {
                fired.Add(payload);
                if (fired.Count >= stopAfterFires)
                {
                    cts.Cancel();
                }

                return Task.CompletedTask;
            },
        };

        try
        {
            await runner.RunAsync(config, context, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        return fired;
    }

    private static MatrixOnMessageConfig Config(string? roomId = null) => new(
        HomeserverUrl: "https://hs.example",
        AccessToken: "syt_token",
        RoomId: roomId);

    [Fact]
    public async Task Fires_for_foreign_messages_and_skips_history_own_and_non_messages()
    {
        var handler = new ScriptedHandler(WhoAmI, InitialSync, SecondSync);

        var fired = await RunAsync(handler, Config(), stopAfterFires: 2);

        // !other:hs also fires (no room filter) — exactly two, never history/own/reactions.
        Assert.Equal(2, fired.Count);
        Assert.Contains(fired, p => p!.Contains("hello jarvis") && p.Contains("@alice:hs") && p.Contains("!r:hs") && p.Contains("$e1"));
        Assert.Contains(fired, p => p!.Contains("other room message"));
        Assert.DoesNotContain(fired, p => p!.Contains("old history message"));
        Assert.DoesNotContain(fired, p => p!.Contains("my own reply"));

        Assert.Contains(handler.Urls, url => url.Contains("/account/whoami"));
        Assert.Contains(handler.Urls, url => url.Contains("since=t1"));
    }

    [Fact]
    public async Task Room_filter_limits_to_the_configured_room()
    {
        var handler = new ScriptedHandler(WhoAmI, InitialSync, SecondSync);

        var fired = await RunAsync(handler, Config(roomId: "!r:hs"), stopAfterFires: 1);

        var payload = Assert.Single(fired);
        Assert.Contains("hello jarvis", payload);
        Assert.DoesNotContain(fired, p => p!.Contains("other room message"));
    }

    [Fact]
    public async Task Sync_failures_throw_for_the_supervisors_backoff()
    {
        var handler = new ScriptedHandler(WhoAmI, "ERROR: sync exploded");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await new OnMessageTrigger().RunAsync(Config(), new AutomateX.Plugin.Sdk.TriggerContext
            {
                Logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                Http = new HttpClient(handler),
                Fire = _ => Task.CompletedTask,
            }, cts.Token);
        });
    }

    [Theory]
    [InlineData("", "token")]
    [InlineData("https://hs.example", "")]
    public async Task Missing_required_config_is_rejected_before_any_call(string homeserver, string token)
    {
        var handler = new ScriptedHandler();
        var config = new MatrixOnMessageConfig(homeserver, token);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await new OnMessageTrigger().RunAsync(config, new AutomateX.Plugin.Sdk.TriggerContext
            {
                Logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                Http = new HttpClient(handler),
                Fire = _ => Task.CompletedTask,
            }, CancellationToken.None);
        });

        Assert.Empty(handler.Urls);
    }

    [Fact]
    public void OnMessage_trigger_is_discoverable_with_schema()
    {
        using var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddLogging()
            .AddHttpClient()
            .AddSingleton<AutomateX.Engine.Actions.ActionContextFactory>()
            .BuildServiceProvider();

        var triggers = TriggerDiscovery.FromAssembly(typeof(OnMessageTrigger).Assembly, "matrix", services).ToList();

        var trigger = Assert.Single(triggers, x => x.Descriptor.Type == "matrix.onMessage");
        Assert.NotNull(trigger.Descriptor.ConfigSchema);
        Assert.Contains("homeserverUrl", trigger.Descriptor.ConfigSchema.ToJsonString());
    }
}
