using System.Net;
using System.Text;
using AutomateX.Engine.Agent;
using AutomateX.Plugin.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutomateX.Tests;

// The agent loop end to end against a single fake transport that scripts both the LLM
// (tool call → final answer) and the MCP server (initialize → tools/list → tools/call).
public sealed class LlmAgentActionTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request, body);
        }
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Mcp(string body) =>
        body.Contains("\"method\":\"initialize\"") ? Json("""{"jsonrpc":"2.0","id":1,"result":{}}""")
        : body.Contains("notifications/initialized") ? new HttpResponseMessage(HttpStatusCode.Accepted)
        : body.Contains("\"method\":\"tools/list\"")
            ? Json("""{"jsonrpc":"2.0","id":2,"result":{"tools":[{"name":"echo","description":"echoes","inputSchema":{"type":"object","properties":{"msg":{"type":"string"}}}}]}}""")
        : Json("""{"jsonrpc":"2.0","id":2,"result":{"content":[{"type":"text","text":"hi"}]}}""");

    private static ActionContext Context(FakeHandler handler) => new()
    {
        Logger = NullLogger.Instance,
        Http = new HttpClient(handler),
        ExecutionId = Guid.CreateVersion7(),
        WorkflowId = Guid.CreateVersion7(),
        StepOrder = 0,
    };

    private static LlmAgentConfig Config(int maxIterations = 8) => new(
        Model: "gpt-x",
        Goal: "echo hi",
        McpServers: [new AgentMcpServer("https://mcp.example/mcp")],
        BaseUrl: "https://llm.example",
        MaxIterations: maxIterations);

    private const string ToolCallReply =
        """{"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[{"id":"c1","type":"function","function":{"name":"echo","arguments":"{\"msg\":\"hi\"}"}}]}}]}""";

    [Fact]
    public async Task Runs_a_tool_then_returns_the_final_answer_with_a_transcript()
    {
        var llmCalls = 0;
        var handler = new FakeHandler((request, body) =>
        {
            if (request.RequestUri!.AbsoluteUri.Contains("/v1/chat/completions"))
            {
                return ++llmCalls == 1
                    ? Json(ToolCallReply)
                    : Json("""{"choices":[{"message":{"role":"assistant","content":"the echo said hi"}}]}""");
            }

            return Mcp(body);
        });

        var result = await new LlmAgentAction().ExecuteAsync(Config(), Context(handler));

        Assert.True(result.Finished);
        Assert.Equal("the echo said hi", result.Output);
        Assert.Equal(2, result.Iterations);
        var trace = Assert.Single(result.Transcript);
        Assert.Equal("echo", trace.Tool);
        Assert.Equal("hi", trace.Result);
        Assert.False(trace.IsError);
    }

    [Fact]
    public async Task Stops_at_the_iteration_cap_unfinished()
    {
        // The model keeps asking for the tool and never produces a final answer.
        var handler = new FakeHandler((request, body) =>
            request.RequestUri!.AbsoluteUri.Contains("/v1/chat/completions") ? Json(ToolCallReply) : Mcp(body));

        var result = await new LlmAgentAction().ExecuteAsync(Config(maxIterations: 2), Context(handler));

        Assert.False(result.Finished);
        Assert.Equal(2, result.Iterations);
        Assert.Equal(2, result.Transcript.Count);
    }
}
