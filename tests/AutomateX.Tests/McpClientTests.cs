using System.Net;
using System.Text;
using System.Text.Json;
using AutomateX.Engine.Mcp;
using Xunit;

namespace AutomateX.Tests;

public sealed class McpClientTests
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

    private static HttpResponseMessage Json(string body, string? session = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (session is not null)
        {
            response.Headers.Add("Mcp-Session-Id", session);
        }

        return response;
    }

    private static HttpResponseMessage Sse(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") };

    private static JsonElement Args(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private static McpServer Server(params (string, string)[] headers) =>
        new("https://server.example/mcp", headers.ToDictionary(x => x.Item1, x => x.Item2));

    private static HttpResponseMessage Route(string body)
    {
        if (body.Contains("\"method\":\"initialize\""))
        {
            return Json("""{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-06-18","serverInfo":{"name":"x"}}}""", session: "sess-1");
        }

        if (body.Contains("notifications/initialized"))
        {
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        // tools/call → answer over SSE to exercise that path.
        return Sse("event: message\ndata: {\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"done\"}]}}\n\n");
    }

    [Fact]
    public async Task Call_tool_initializes_then_calls_with_the_session_and_headers()
    {
        var handler = new FakeHandler(Route);
        var client = new McpClient(new HttpClient(handler));

        var result = await client.CallToolAsync(Server(("Authorization", "Bearer t")), "do_thing", Args("""{"x":1}"""), default);

        Assert.Equal("done", result.Text);
        Assert.False(result.IsError);

        // initialize, notifications/initialized, tools/call
        Assert.Equal(3, handler.Calls.Count);
        var toolCall = handler.Calls[2];
        Assert.Contains("\"method\":\"tools/call\"", toolCall.Body);
        Assert.Equal("sess-1", toolCall.Request.Headers.GetValues("Mcp-Session-Id").Single());
        Assert.Equal("Bearer t", toolCall.Request.Headers.GetValues("Authorization").Single());
        Assert.Equal("2025-06-18", toolCall.Request.Headers.GetValues("MCP-Protocol-Version").Single());
    }

    [Fact]
    public async Task Tool_error_is_surfaced_as_a_flagged_result()
    {
        var handler = new FakeHandler(body =>
            body.Contains("\"method\":\"initialize\"") ? Json("""{"jsonrpc":"2.0","id":1,"result":{}}""")
            : body.Contains("notifications/initialized") ? new HttpResponseMessage(HttpStatusCode.Accepted)
            : Json("""{"jsonrpc":"2.0","id":2,"result":{"content":[{"type":"text","text":"nope"}],"isError":true}}"""));

        var result = await new McpClient(new HttpClient(handler)).CallToolAsync(Server(), "t", Args("{}"), default);

        Assert.True(result.IsError);
        Assert.Equal("nope", result.Text);
    }

    [Fact]
    public async Task Non_success_status_throws()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<McpException>(
            () => new McpClient(new HttpClient(handler)).CallToolAsync(Server(), "t", Args("{}"), default));
    }
}
