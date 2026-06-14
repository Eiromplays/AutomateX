using System.Net;
using System.Xml;
using AutomateX.Plugin.Sdk;
using AutomateX.Plugins.Feed;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutomateX.Tests;

// Feed triggers poll on their own loop and dedup via the trigger state, so an item
// fires at most once and a restart never replays. Rules pinned here:
//  RSS: first poll establishes a baseline silently (no backlog blast) unless
//       FireOnFirstPoll; thereafter each genuinely new item fires exactly once;
//       Atom and RSS both parse; a malformed feed throws into the host's backoff.
//  http.poll: fires when the response body changes, stays silent when it doesn't.
public sealed class FeedTriggerTests
{
    // --- RSS ---------------------------------------------------------------

    private const string RssAB =
        """
        <?xml version="1.0"?>
        <rss version="2.0"><channel><title>News</title>
          <item><guid>id-A</guid><title>Item A</title><link>http://x/a</link></item>
          <item><guid>id-B</guid><title>Item B</title><link>http://x/b</link></item>
        </channel></rss>
        """;

    private const string RssABC =
        """
        <?xml version="1.0"?>
        <rss version="2.0"><channel><title>News</title>
          <item><guid>id-C</guid><title>Item C</title><link>http://x/c</link></item>
          <item><guid>id-A</guid><title>Item A</title><link>http://x/a</link></item>
          <item><guid>id-B</guid><title>Item B</title><link>http://x/b</link></item>
        </channel></rss>
        """;

    private const string AtomX =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom"><title>News</title>
          <entry><id>id-X</id><title>Entry X</title><link href="http://x/x"/></entry>
        </feed>
        """;

    [Fact]
    public async Task First_poll_is_a_silent_baseline_then_only_new_items_fire()
    {
        var handler = new ScriptedHandler(RssAB, RssABC);

        var fired = await RunRssAsync(handler, RssConfig(), stopAfterFires: 1);

        var payload = Assert.Single(fired);
        Assert.Contains("id-C", payload);
        Assert.DoesNotContain(fired, p => p!.Contains("id-A") || p.Contains("id-B"));
    }

    [Fact]
    public async Task FireOnFirstPoll_emits_existing_items()
    {
        var handler = new ScriptedHandler(RssAB);

        var fired = await RunRssAsync(handler, RssConfig() with { FireOnFirstPoll = true }, stopAfterFires: 2);

        Assert.Equal(2, fired.Count);
        Assert.Contains(fired, p => p!.Contains("id-A"));
        Assert.Contains(fired, p => p!.Contains("id-B"));
    }

    [Fact]
    public async Task Atom_feeds_parse_too()
    {
        var handler = new ScriptedHandler(AtomX);

        var fired = await RunRssAsync(handler, RssConfig() with { FireOnFirstPoll = true }, stopAfterFires: 1);

        var payload = Assert.Single(fired);
        Assert.Contains("id-X", payload);
    }

    [Fact]
    public async Task Already_seen_items_never_refire()
    {
        var state = new FakeTriggerState();
        // Pre-record A and B (the per-item dedup key the trigger uses) + baseline.
        await state.SetAsync("item:id-A", "1");
        await state.SetAsync("item:id-B", "1");
        await state.SetAsync("baseline", "1");
        var handler = new ScriptedHandler(RssAB);

        // No new items, FireOnFirstPoll off, baseline already set -> nothing fires.
        var fired = await RunRssAsync(handler, RssConfig(), stopAfterFires: 1, state: state, timeout: TimeSpan.FromSeconds(2));

        Assert.Empty(fired);
    }

    [Fact]
    public async Task Malformed_feed_throws_for_the_backoff()
    {
        var handler = new ScriptedHandler("this is not xml");
        var context = Context(new FakeTriggerState(), handler, _ => Task.CompletedTask, out _);

        await Assert.ThrowsAsync<XmlException>(() =>
            new RssTrigger().RunAsync(RssConfig() with { FireOnFirstPoll = true }, context, CancellationToken.None));
    }

    // --- http.poll ---------------------------------------------------------

    [Fact]
    public async Task Http_poll_fires_when_the_body_changes()
    {
        var handler = new ScriptedHandler("snapshot-one", "snapshot-two");

        var fired = await RunHttpPollAsync(handler, HttpConfig(), stopAfterFires: 1);

        var payload = Assert.Single(fired);
        Assert.Contains("snapshot-two", payload);
    }

    [Fact]
    public async Task Http_poll_is_silent_when_the_body_is_unchanged()
    {
        var handler = new ScriptedHandler("same", "same");

        var fired = await RunHttpPollAsync(handler, HttpConfig(), stopAfterFires: 1, timeout: TimeSpan.FromSeconds(2));

        Assert.Empty(fired);
    }

    [Fact]
    public async Task Http_poll_FireOnFirstPoll_emits_the_first_snapshot()
    {
        var handler = new ScriptedHandler("snapshot-one");

        var fired = await RunHttpPollAsync(handler, HttpConfig() with { FireOnFirstPoll = true }, stopAfterFires: 1);

        var payload = Assert.Single(fired);
        Assert.Contains("snapshot-one", payload);
    }

    [Fact]
    public async Task Http_poll_sends_configured_headers()
    {
        var handler = new ScriptedHandler("snapshot-one");
        var config = HttpConfig() with
        {
            FireOnFirstPoll = true,
            Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer tok", ["Accept"] = "application/json" },
        };

        await RunHttpPollAsync(handler, config, stopAfterFires: 1);

        Assert.Equal("Bearer tok", handler.LastRequest!.Headers.GetValues("Authorization").Single());
        Assert.Equal("application/json", handler.LastRequest!.Headers.GetValues("Accept").Single());
    }

    [Fact]
    public async Task Http_poll_ignores_non_2xx_responses()
    {
        // Each call returns a distinct body, so without the 2xx gate the changing 404s would fire.
        var handler = new StatusHandler(HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound);

        var fired = await RunHttpPollAsync(
            handler, HttpConfig() with { FireOnFirstPoll = true }, stopAfterFires: 1, timeout: TimeSpan.FromSeconds(2));

        Assert.Empty(fired);
    }

    // --- helpers -----------------------------------------------------------

    private static RssTriggerConfig RssConfig() => new(Url: "https://feed.example/rss", PollSeconds: 0);

    private static HttpPollTriggerConfig HttpConfig() => new(Url: "https://api.example/state", PollSeconds: 0);

    private static Task<List<string?>> RunRssAsync(
        ScriptedHandler handler, RssTriggerConfig config, int stopAfterFires,
        ITriggerState? state = null, TimeSpan? timeout = null) =>
        RunAsync((cfg, ctx, ct) => new RssTrigger().RunAsync((RssTriggerConfig)cfg, ctx, ct),
            config, handler, stopAfterFires, state, timeout);

    private static Task<List<string?>> RunHttpPollAsync(
        HttpMessageHandler handler, HttpPollTriggerConfig config, int stopAfterFires,
        ITriggerState? state = null, TimeSpan? timeout = null) =>
        RunAsync((cfg, ctx, ct) => new HttpPollTrigger().RunAsync((HttpPollTriggerConfig)cfg, ctx, ct),
            config, handler, stopAfterFires, state, timeout);

    private static async Task<List<string?>> RunAsync(
        Func<object, TriggerContext, CancellationToken, Task> run,
        object config, HttpMessageHandler handler, int stopAfterFires, ITriggerState? state, TimeSpan? timeout)
    {
        List<string?> fired = [];
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        var context = Context(state ?? new FakeTriggerState(), handler, payload =>
        {
            fired.Add(payload);
            if (fired.Count >= stopAfterFires)
            {
                cts.Cancel();
            }

            return Task.CompletedTask;
        }, out _);

        try
        {
            await run(config, context, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        return fired;
    }

    private static TriggerContext Context(
        ITriggerState state, HttpMessageHandler handler, Func<string?, Task> fire, out HttpClient http)
    {
        http = new HttpClient(handler);
        return new TriggerContext
        {
            Logger = NullLogger.Instance,
            Http = http,
            TriggerId = Guid.CreateVersion7(),
            WorkflowId = Guid.CreateVersion7(),
            State = state,
            Fire = fire,
        };
    }

    private sealed class ScriptedHandler(params string[] responses) : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new(responses);

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (_responses.Count == 0)
            {
                // Script exhausted: park until the test cancels.
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_responses.Dequeue()) };
        }
    }

    // Returns the scripted status codes (then parks) — for the 2xx-only rule. Each call gets a
    // distinct body, so a non-2xx would fire if the trigger didn't gate on status.
    private sealed class StatusHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _statuses = new(statuses);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_statuses.Count == 0)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new HttpResponseMessage(_statuses.Dequeue()) { Content = new StringContent(Guid.NewGuid().ToString()) };
        }
    }

    private sealed class FakeTriggerState : ITriggerState
    {
        private readonly Dictionary<string, string> _store = new();

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.TryGetValue(key, out var value) ? value : null);

        public Task SetAsync(string key, string value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
        {
            _store[key] = value;
            return Task.CompletedTask;
        }

        public Task<bool> SetIfAbsentAsync(string key, string value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
        {
            if (_store.ContainsKey(key))
            {
                return Task.FromResult(false);
            }

            _store[key] = value;
            return Task.FromResult(true);
        }

        public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.Remove(key));
    }
}
