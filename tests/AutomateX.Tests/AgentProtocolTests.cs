using System.Text.Json;
using AutomateX.Engine.Agent;
using AutomateX.Engine.Mcp;
using Xunit;

namespace AutomateX.Tests;

public sealed class AgentProtocolTests
{
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    [Fact]
    public void ToOpenAiTools_maps_name_description_and_schema()
    {
        var tools = AgentProtocol.ToOpenAiTools([
            new McpTool("create_issue", "Opens an issue", Json("""{"type":"object","properties":{"title":{"type":"string"}}}""")),
        ]);

        var fn = tools[0]!["function"]!;
        Assert.Equal("function", tools[0]!["type"]!.GetValue<string>());
        Assert.Equal("create_issue", fn["name"]!.GetValue<string>());
        Assert.Equal("Opens an issue", fn["description"]!.GetValue<string>());
        Assert.Equal("string", fn["parameters"]!["properties"]!["title"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void ToOpenAiTools_defaults_schema_when_missing_and_omits_null_description()
    {
        var tools = AgentProtocol.ToOpenAiTools([new McpTool("ping", null, default)]);

        var fn = tools[0]!["function"]!;
        Assert.Equal("object", fn["parameters"]!["type"]!.GetValue<string>());
        Assert.Null(fn["description"]);
    }

    [Fact]
    public void ParseTurn_reads_tool_calls()
    {
        var turn = AgentProtocol.ParseTurn("""
            {"choices":[{"message":{"role":"assistant","content":null,
              "tool_calls":[{"id":"call_1","type":"function","function":{"name":"create_issue","arguments":"{\"title\":\"hi\"}"}}]}}]}
            """);

        Assert.Null(turn.FinalContent);
        var call = Assert.Single(turn.ToolCalls);
        Assert.Equal("call_1", call.Id);
        Assert.Equal("create_issue", call.Name);
        Assert.Equal("{\"title\":\"hi\"}", call.ArgumentsJson);
        Assert.NotNull(turn.AssistantMessage); // appended verbatim before the tool results
    }

    [Fact]
    public void ParseTurn_reads_a_final_answer()
    {
        var turn = AgentProtocol.ParseTurn("""{"choices":[{"message":{"role":"assistant","content":"all done"}}]}""");

        Assert.Equal("all done", turn.FinalContent);
        Assert.Empty(turn.ToolCalls);
        Assert.Null(turn.AssistantMessage);
    }

    [Fact]
    public void ParseTurn_throws_without_choices()
    {
        Assert.Throws<InvalidOperationException>(() => AgentProtocol.ParseTurn("""{"choices":[]}"""));
    }

    [Fact]
    public void ToolResultMessage_has_the_tool_role_shape()
    {
        var message = AgentProtocol.ToolResultMessage("call_1", "result text");

        Assert.Equal("tool", message["role"]!.GetValue<string>());
        Assert.Equal("call_1", message["tool_call_id"]!.GetValue<string>());
        Assert.Equal("result text", message["content"]!.GetValue<string>());
    }
}
