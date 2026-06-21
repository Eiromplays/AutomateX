using System.Net;
using System.Text;
using AutomateX.Engine;
using AutomateX.Engine.Actions;
using AutomateX.Modules.Triggers;
using AutomateX.Plugin.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutomateX.Tests;

// webhook.send: POST the body to a URL, optionally HMAC-signing it (matching the inbound webhook
// format). Non-2xx fails the step; validation happens before any call.
public sealed class WebhookSendActionTests
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

    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK) { Content = new StringContent("received") };

    private static WebhookSendAction Action() => new(Options.Create(new EngineOptions()));

    private static ActionContext Context(FakeHandler handler) => new()
    {
        Logger = NullLogger.Instance,
        Http = new HttpClient(handler),
        ExecutionId = Guid.CreateVersion7(),
        WorkflowId = Guid.CreateVersion7(),
        StepOrder = 0,
    };

    private static WebhookSendConfig Config(string body = """{"hello":"world"}""") => new(
        Url: "https://example.com/hook",
        Body: body);

    [Fact]
    public async Task Posts_body_to_the_url()
    {
        var handler = new FakeHandler(_ => Ok());

        var result = await Action().ExecuteAsync(Config(), Context(handler));

        var (request, body) = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://example.com/hook", request.RequestUri!.ToString());
        Assert.Equal("""{"hello":"world"}""", body);
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("received", result.Body);
    }

    [Fact]
    public async Task Signs_the_body_with_the_inbound_format()
    {
        var handler = new FakeHandler(_ => Ok());
        var config = Config() with { SigningSecret = "s3cret" };

        await Action().ExecuteAsync(config, Context(handler));

        var (request, body) = Assert.Single(handler.Calls);
        var signature = Assert.Single(request.Headers.GetValues(WebhookSecret.SignatureHeader));
        Assert.Equal(WebhookSecret.Sign("s3cret", body), signature);
        Assert.StartsWith("sha256=", signature);
    }

    [Fact]
    public async Task Signature_header_name_can_be_overridden()
    {
        var handler = new FakeHandler(_ => Ok());
        var config = Config() with { SigningSecret = "s3cret", SignatureHeader = "X-Hub-Signature-256" };

        await Action().ExecuteAsync(config, Context(handler));

        var (request, _) = Assert.Single(handler.Calls);
        Assert.True(request.Headers.Contains("X-Hub-Signature-256"));
        Assert.False(request.Headers.Contains(WebhookSecret.SignatureHeader));
    }

    [Fact]
    public async Task No_signature_header_without_a_secret()
    {
        var handler = new FakeHandler(_ => Ok());

        await Action().ExecuteAsync(Config(), Context(handler));

        var (request, _) = Assert.Single(handler.Calls);
        Assert.False(request.Headers.Contains(WebhookSecret.SignatureHeader));
    }

    [Fact]
    public async Task Custom_headers_are_sent()
    {
        var handler = new FakeHandler(_ => Ok());
        var config = Config() with { Headers = new Dictionary<string, string> { ["X-Trace"] = "abc" } };

        await Action().ExecuteAsync(config, Context(handler));

        var (request, _) = Assert.Single(handler.Calls);
        Assert.Equal("abc", Assert.Single(request.Headers.GetValues("X-Trace")));
    }

    [Fact]
    public async Task Non_success_status_throws_with_body()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("boom", Encoding.UTF8, "text/plain"),
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Action().ExecuteAsync(Config(), Context(handler)));

        Assert.Contains("500", exception.Message);
        Assert.Contains("boom", exception.Message);
    }

    [Theory]
    [InlineData("", "{}")]
    [InlineData("https://example.com/hook", "")]
    public async Task Invalid_config_is_rejected_before_sending(string url, string body)
    {
        var handler = new FakeHandler(_ => Ok());

        await Assert.ThrowsAsync<ArgumentException>(
            () => Action().ExecuteAsync(new WebhookSendConfig(url, body), Context(handler)));

        Assert.Empty(handler.Calls);
    }

    [Fact]
    public void Webhook_send_is_discoverable_as_a_builtin_with_schema()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddHttpClient()
            .AddSingleton<ActionContextFactory>()
            .BuildServiceProvider();

        var actions = ActionDiscovery.FromAssembly(typeof(WebhookSendAction).Assembly, "builtin", services).ToList();

        var action = Assert.Single(actions, x => x.Descriptor.Type == "webhook.send");
        Assert.NotNull(action.Descriptor.ConfigSchema);
        Assert.Contains("signingSecret", action.Descriptor.ConfigSchema.ToJsonString());
    }
}
