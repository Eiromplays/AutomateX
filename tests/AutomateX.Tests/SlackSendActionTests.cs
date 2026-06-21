using System.Net;
using System.Text;
using AutomateX.Engine.Actions;
using AutomateX.Plugin.Sdk;
using AutomateX.Plugins.Slack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutomateX.Tests;

// slack.send: POST the text to an incoming webhook. Slack returns 200 "ok" on success and a
// non-2xx with an error string otherwise. Validation happens before any call.
public sealed class SlackSendActionTests
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

    private static HttpResponseMessage Ok() =>
        new(HttpStatusCode.OK) { Content = new StringContent("ok") };

    private static ActionContext Context(FakeHandler handler) => new()
    {
        Logger = NullLogger.Instance,
        Http = new HttpClient(handler),
        ExecutionId = Guid.CreateVersion7(),
        WorkflowId = Guid.CreateVersion7(),
        StepOrder = 0,
    };

    private static SlackSendConfig Config(string text = "hello") => new(
        WebhookUrl: "https://hooks.slack.com/services/T0/B0/xyz",
        Text: text);

    [Fact]
    public async Task Posts_text_to_the_webhook()
    {
        var handler = new FakeHandler(_ => Ok());

        var result = await new SendMessageAction().ExecuteAsync(Config(), Context(handler));

        var (request, body) = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://hooks.slack.com/services/T0/B0/xyz", request.RequestUri!.ToString());
        Assert.Contains("\"text\":\"hello\"", body);
        Assert.Equal(200, result.StatusCode);
    }

    [Fact]
    public async Task Username_and_icon_are_sent_when_set()
    {
        var handler = new FakeHandler(_ => Ok());

        await new SendMessageAction().ExecuteAsync(
            Config() with { Username = "AutomateX", IconEmoji = ":robot_face:" }, Context(handler));

        var (_, body) = Assert.Single(handler.Calls);
        Assert.Contains("\"username\":\"AutomateX\"", body);
        Assert.Contains("\"icon_emoji\":\":robot_face:\"", body);
    }

    [Fact]
    public async Task Optional_fields_are_omitted_when_unset()
    {
        var handler = new FakeHandler(_ => Ok());

        await new SendMessageAction().ExecuteAsync(Config(), Context(handler));

        var (_, body) = Assert.Single(handler.Calls);
        Assert.DoesNotContain("username", body);
        Assert.DoesNotContain("icon_emoji", body);
    }

    [Fact]
    public async Task Failure_status_throws_with_details()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("invalid_payload", Encoding.UTF8, "text/plain"),
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SendMessageAction().ExecuteAsync(Config(), Context(handler)));

        Assert.Contains("400", exception.Message);
        Assert.Contains("invalid_payload", exception.Message);
    }

    [Theory]
    [InlineData("", "hello")]
    [InlineData("https://hooks.slack.com/services/T0/B0/xyz", "")]
    public async Task Invalid_config_is_rejected_before_sending(string webhookUrl, string text)
    {
        var handler = new FakeHandler(_ => Ok());

        await Assert.ThrowsAsync<ArgumentException>(
            () => new SendMessageAction().ExecuteAsync(new SlackSendConfig(webhookUrl, text), Context(handler)));

        Assert.Empty(handler.Calls);
    }

    [Theory]
    [InlineData("https://hooks.slack.com/services/T0/B0/xyz", true)]
    [InlineData("https://example.com/webhook", false)]
    [InlineData("http://hooks.slack.com/services/T0/B0/xyz", false)]
    public async Task Connection_test_validates_the_webhook_shape(string url, bool expected)
    {
        var result = await new SlackConnectionType().TestAsync(
            new Dictionary<string, string> { ["webhookUrl"] = url }, new HttpClient(), CancellationToken.None);

        Assert.Equal(expected, result.Ok);
    }

    [Fact]
    public async Task Connection_test_reports_missing_webhook()
    {
        var result = await new SlackConnectionType().TestAsync(
            new Dictionary<string, string>(), new HttpClient(), CancellationToken.None);

        Assert.False(result.Ok);
    }

    [Fact]
    public void Slack_action_is_discoverable_with_schema()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddHttpClient()
            .AddSingleton<ActionContextFactory>()
            .BuildServiceProvider();

        var actions = ActionDiscovery.FromAssembly(typeof(SendMessageAction).Assembly, "slack", services).ToList();

        var action = Assert.Single(actions, x => x.Descriptor.Type == "slack.send");
        Assert.NotNull(action.Descriptor.ConfigSchema);
        Assert.Contains("webhookUrl", action.Descriptor.ConfigSchema.ToJsonString());
    }
}
