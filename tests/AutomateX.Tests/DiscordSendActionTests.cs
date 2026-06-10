using System.Net;
using System.Text;
using AutomateX.Engine.Actions;
using AutomateX.Plugin.Sdk;
using AutomateX.Plugins.Discord;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutomateX.Tests;

// discord.send: POST the content to the webhook URL. A webhook returns 204 No Content
// on success; a non-2xx fails the step with the body. Validation happens before any call.
public sealed class DiscordSendActionTests
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

    private static HttpResponseMessage NoContent() => new(HttpStatusCode.NoContent);

    private static ActionContext Context(FakeHandler handler) => new()
    {
        Logger = NullLogger.Instance,
        Http = new HttpClient(handler),
        ExecutionId = Guid.CreateVersion7(),
        WorkflowId = Guid.CreateVersion7(),
        StepOrder = 0,
    };

    private static DiscordSendConfig Config(string content = "hello") => new(
        WebhookUrl: "https://discord.com/api/webhooks/123/abc",
        Content: content);

    [Fact]
    public async Task Posts_content_to_the_webhook()
    {
        var handler = new FakeHandler(_ => NoContent());

        var result = await new SendMessageAction().ExecuteAsync(Config(), Context(handler));

        var (request, body) = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://discord.com/api/webhooks/123/abc", request.RequestUri!.ToString());
        Assert.Contains("\"content\":\"hello\"", body);
        Assert.Equal(204, result.StatusCode);
    }

    [Fact]
    public async Task Username_override_is_sent_when_set()
    {
        var handler = new FakeHandler(_ => NoContent());

        await new SendMessageAction().ExecuteAsync(Config() with { Username = "AutomateX" }, Context(handler));

        var (_, body) = Assert.Single(handler.Calls);
        Assert.Contains("\"username\":\"AutomateX\"", body);
    }

    [Fact]
    public async Task Username_is_omitted_when_not_set()
    {
        var handler = new FakeHandler(_ => NoContent());

        await new SendMessageAction().ExecuteAsync(Config(), Context(handler));

        var (_, body) = Assert.Single(handler.Calls);
        Assert.DoesNotContain("username", body);
    }

    [Fact]
    public async Task Failure_status_throws_with_details()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"message":"Cannot send an empty message"}""", Encoding.UTF8, "application/json"),
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SendMessageAction().ExecuteAsync(Config(), Context(handler)));

        Assert.Contains("400", exception.Message);
        Assert.Contains("empty message", exception.Message);
    }

    [Theory]
    [InlineData("", "hello")]
    [InlineData("https://discord.com/api/webhooks/1/x", "")]
    public async Task Invalid_config_is_rejected_before_sending(string webhookUrl, string content)
    {
        var handler = new FakeHandler(_ => NoContent());
        var config = new DiscordSendConfig(webhookUrl, content);

        await Assert.ThrowsAsync<ArgumentException>(
            () => new SendMessageAction().ExecuteAsync(config, Context(handler)));

        Assert.Empty(handler.Calls);
    }

    [Fact]
    public void Discord_action_is_discoverable_with_schema()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddHttpClient()
            .AddSingleton<ActionContextFactory>()
            .BuildServiceProvider();

        var actions = ActionDiscovery.FromAssembly(typeof(SendMessageAction).Assembly, "discord", services).ToList();

        var action = Assert.Single(actions, x => x.Descriptor.Type == "discord.send");
        Assert.NotNull(action.Descriptor.ConfigSchema);
        Assert.Contains("webhookUrl", action.Descriptor.ConfigSchema.ToJsonString());
    }
}
