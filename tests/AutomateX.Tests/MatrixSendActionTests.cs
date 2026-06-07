using System.Net;
using System.Text;
using AutomateX.Engine.Actions;
using AutomateX.Plugin.Sdk;
using AutomateX.Plugins.Matrix;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutomateX.Tests;

public sealed class MatrixSendActionTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request, body));
            return respond(request);
        }
    }

    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("""{"event_id":"$evt123"}""", Encoding.UTF8, "application/json"),
    };

    private static ActionContext Context(FakeHandler handler, Guid? executionId = null, int stepOrder = 0) => new()
    {
        Logger = NullLogger.Instance,
        Http = new HttpClient(handler),
        ExecutionId = executionId ?? Guid.CreateVersion7(),
        WorkflowId = Guid.CreateVersion7(),
        StepOrder = stepOrder,
    };

    private static MatrixSendConfig Config(string message = "hello", string? html = null) => new(
        HomeserverUrl: "https://matrix.example.org",
        AccessToken: "syt_secret_token",
        RoomId: "!room:example.org",
        Message: message,
        Html: html);

    [Fact]
    public async Task Sends_put_with_bearer_token_and_text_body()
    {
        var handler = new FakeHandler(_ => Ok());

        var result = await new SendMessageAction().ExecuteAsync(Config(), Context(handler));

        var (request, body) = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.StartsWith(
            "https://matrix.example.org/_matrix/client/v3/rooms/%21room%3Aexample.org/send/m.room.message/",
            request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("syt_secret_token", request.Headers.Authorization?.Parameter);
        Assert.Contains("\"msgtype\":\"m.text\"", body);
        Assert.Contains("\"body\":\"hello\"", body);
        Assert.Equal("$evt123", result.EventId);
    }

    [Fact]
    public async Task Transaction_id_is_deterministic_per_execution_step()
    {
        var handler = new FakeHandler(_ => Ok());
        var executionId = Guid.CreateVersion7();

        await new SendMessageAction().ExecuteAsync(Config(), Context(handler, executionId, stepOrder: 1));
        await new SendMessageAction().ExecuteAsync(Config(), Context(handler, executionId, stepOrder: 1));
        await new SendMessageAction().ExecuteAsync(Config(), Context(handler, executionId, stepOrder: 2));

        var txnIds = handler.Calls.Select(x => x.Request.RequestUri!.Segments[^1]).ToList();
        Assert.Equal(txnIds[0], txnIds[1]); // engine retry replays the same txnId — homeserver dedupes
        Assert.NotEqual(txnIds[0], txnIds[2]); // a different step is a different message
    }

    [Fact]
    public async Task Html_message_adds_formatted_body()
    {
        var handler = new FakeHandler(_ => Ok());

        await new SendMessageAction().ExecuteAsync(Config(html: "<b>hello</b>"), Context(handler));

        var (_, body) = Assert.Single(handler.Calls);
        Assert.Contains("org.matrix.custom.html", body);
        Assert.Contains("\"formatted_body\":", body);
    }

    [Fact]
    public async Task Failure_status_throws_with_details()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{"errcode":"M_FORBIDDEN"}""", Encoding.UTF8, "application/json"),
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SendMessageAction().ExecuteAsync(Config(), Context(handler)));

        Assert.Contains("403", exception.Message);
        Assert.Contains("M_FORBIDDEN", exception.Message);
    }

    [Theory]
    [InlineData("", "token", "!r:s", "msg")]
    [InlineData("https://hs", "", "!r:s", "msg")]
    [InlineData("https://hs", "token", "", "msg")]
    [InlineData("https://hs", "token", "!r:s", "")]
    public async Task Invalid_config_is_rejected_before_sending(
        string homeserver, string token, string roomId, string message)
    {
        var handler = new FakeHandler(_ => Ok());
        var config = new MatrixSendConfig(homeserver, token, roomId, message);

        await Assert.ThrowsAsync<ArgumentException>(
            () => new SendMessageAction().ExecuteAsync(config, Context(handler)));

        Assert.Empty(handler.Calls);
    }

    [Fact]
    public void Matrix_action_is_discoverable_with_schema()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddHttpClient()
            .AddSingleton<ActionContextFactory>()
            .BuildServiceProvider();

        var actions = ActionDiscovery.FromAssembly(typeof(SendMessageAction).Assembly, "matrix", services).ToList();

        var action = Assert.Single(actions, x => x.Descriptor.Type == "matrix.send");
        Assert.NotNull(action.Descriptor.ConfigSchema);
        Assert.Contains("roomId", action.Descriptor.ConfigSchema.ToJsonString());
    }
}
