using System.Net;
using System.Text;
using AutomateX.Engine.Actions;
using AutomateX.Plugin.Sdk;
using AutomateX.Plugins.Pushover;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutomateX.Tests;

// pushover.send: form-POST token/user/message to the Pushover messages API. Optional
// title + priority; priority is range-checked. Non-2xx fails the step; validation first.
public sealed class PushoverSendActionTests
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
        Content = new StringContent("""{"status":1,"request":"req-123"}""", Encoding.UTF8, "application/json"),
    };

    private static ActionContext Context(FakeHandler handler) => new()
    {
        Logger = NullLogger.Instance,
        Http = new HttpClient(handler),
        ExecutionId = Guid.CreateVersion7(),
        WorkflowId = Guid.CreateVersion7(),
        StepOrder = 0,
    };

    private static PushoverSendConfig Config(string message = "hello") => new(
        AppToken: "app-token",
        UserKey: "user-key",
        Message: message);

    [Fact]
    public async Task Form_posts_token_user_and_message()
    {
        var handler = new FakeHandler(_ => Ok());

        var result = await new SendMessageAction().ExecuteAsync(Config(), Context(handler));

        var (request, body) = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.pushover.net/1/messages.json", request.RequestUri!.ToString());
        Assert.Contains("token=app-token", body);
        Assert.Contains("user=user-key", body);
        Assert.Contains("message=hello", body);
        Assert.Equal(1, result.Status);
        Assert.Equal("req-123", result.Request);
    }

    [Fact]
    public async Task Title_and_priority_are_sent_when_set()
    {
        var handler = new FakeHandler(_ => Ok());

        await new SendMessageAction().ExecuteAsync(
            Config() with { Title = "Oilers", Priority = 1 }, Context(handler));

        var (_, body) = Assert.Single(handler.Calls);
        Assert.Contains("title=Oilers", body);
        Assert.Contains("priority=1", body);
    }

    [Fact]
    public async Task Optional_fields_are_omitted_when_unset()
    {
        var handler = new FakeHandler(_ => Ok());

        await new SendMessageAction().ExecuteAsync(Config(), Context(handler));

        var (_, body) = Assert.Single(handler.Calls);
        Assert.DoesNotContain("title=", body);
        Assert.DoesNotContain("priority=", body);
    }

    [Fact]
    public async Task Failure_status_throws_with_details()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"status":0,"errors":["application token is invalid"]}""", Encoding.UTF8, "application/json"),
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SendMessageAction().ExecuteAsync(Config(), Context(handler)));

        Assert.Contains("400", exception.Message);
        Assert.Contains("token is invalid", exception.Message);
    }

    [Fact]
    public async Task Out_of_range_priority_is_rejected_before_sending()
    {
        var handler = new FakeHandler(_ => Ok());

        await Assert.ThrowsAsync<ArgumentException>(
            () => new SendMessageAction().ExecuteAsync(Config() with { Priority = 3 }, Context(handler)));

        Assert.Empty(handler.Calls);
    }

    [Theory]
    [InlineData("", "user", "msg")]
    [InlineData("token", "", "msg")]
    [InlineData("token", "user", "")]
    public async Task Invalid_config_is_rejected_before_sending(string appToken, string userKey, string message)
    {
        var handler = new FakeHandler(_ => Ok());
        var config = new PushoverSendConfig(appToken, userKey, message);

        await Assert.ThrowsAsync<ArgumentException>(
            () => new SendMessageAction().ExecuteAsync(config, Context(handler)));

        Assert.Empty(handler.Calls);
    }

    [Fact]
    public void Pushover_action_is_discoverable_with_schema()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddHttpClient()
            .AddSingleton<ActionContextFactory>()
            .BuildServiceProvider();

        var actions = ActionDiscovery.FromAssembly(typeof(SendMessageAction).Assembly, "pushover", services).ToList();

        var action = Assert.Single(actions, x => x.Descriptor.Type == "pushover.send");
        Assert.NotNull(action.Descriptor.ConfigSchema);
        Assert.Contains("userKey", action.Descriptor.ConfigSchema.ToJsonString());
    }
}
