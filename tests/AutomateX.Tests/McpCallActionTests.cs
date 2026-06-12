using System.Net;
using System.Text;
using System.Text.Json;
using AutomateX.Engine.Mcp;
using AutomateX.Plugin.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutomateX.Tests;

public sealed class McpCallActionTests
{
    private sealed class FakeHandler(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request, body));
            return respond(body);
        }
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Route(string toolCallResult) => Json(toolCallResult);

    private static FakeHandler Handler(string toolCallResultJson) => new(body =>
        body.Contains("\"method\":\"initialize\"") ? Json("""{"jsonrpc":"2.0","id":1,"result":{}}""")
        : body.Contains("notifications/initialized") ? new HttpResponseMessage(HttpStatusCode.Accepted)
        : Route(toolCallResultJson));

    private static ActionContext Context(FakeHandler handler) => new()
    {
        Logger = NullLogger.Instance,
        Http = new HttpClient(handler),
        ExecutionId = Guid.CreateVersion7(),
        WorkflowId = Guid.CreateVersion7(),
        StepOrder = 0,
    };

    private static McpCallConfig Config(string? token = "t") =>
        new("https://server.example/mcp", "do_thing", JsonDocument.Parse("{}").RootElement.Clone(), Token: token);

    [Fact]
    public async Task Token_is_sent_as_a_bearer_authorization_header()
    {
        var handler = Handler("""{"jsonrpc":"2.0","id":2,"result":{"content":[{"type":"text","text":"ok"}]}}""");

        var result = await new McpCallAction().ExecuteAsync(Config(), Context(handler));

        Assert.Equal("ok", result.Text);
        Assert.Equal("Bearer t", handler.Calls[2].Request.Headers.GetValues("Authorization").Single());
    }

    [Fact]
    public async Task Tool_error_fails_the_step()
    {
        var handler = Handler("""{"jsonrpc":"2.0","id":2,"result":{"content":[{"type":"text","text":"bad input"}],"isError":true}}""");

        var ex = await Assert.ThrowsAsync<McpException>(() => new McpCallAction().ExecuteAsync(Config(), Context(handler)));
        Assert.Contains("bad input", ex.Message);
    }
}
