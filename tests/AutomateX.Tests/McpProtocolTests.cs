using System.Text.Json;
using AutomateX.Engine.Mcp;
using Xunit;

namespace AutomateX.Tests;

public sealed class McpProtocolTests
{
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    [Fact]
    public void BuildRequest_is_valid_json_rpc()
    {
        var json = McpProtocol.BuildRequest(7, "tools/call", McpProtocol.ToolCallParams("create_issue", Json("""{"title":"hi"}""")));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal(7, root.GetProperty("id").GetInt32());
        Assert.Equal("tools/call", root.GetProperty("method").GetString());
        Assert.Equal("create_issue", root.GetProperty("params").GetProperty("name").GetString());
        Assert.Equal("hi", root.GetProperty("params").GetProperty("arguments").GetProperty("title").GetString());
    }

    [Fact]
    public void BuildRequest_omits_params_when_null_and_notification_has_no_id()
    {
        using var request = JsonDocument.Parse(McpProtocol.BuildRequest(1, "ping", null));
        Assert.False(request.RootElement.TryGetProperty("params", out _));

        using var notification = JsonDocument.Parse(McpProtocol.BuildNotification("notifications/initialized", null));
        Assert.False(notification.RootElement.TryGetProperty("id", out _));
        Assert.Equal("notifications/initialized", notification.RootElement.GetProperty("method").GetString());
    }

    [Fact]
    public void ParseSse_extracts_data_payloads()
    {
        const string body = ": a comment\nevent: message\ndata: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}\n\nevent: message\ndata: {\"x\":\ndata: 1}\n\n";
        var messages = McpProtocol.ParseSseMessages(body).ToList();

        Assert.Equal(2, messages.Count);
        Assert.Equal("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}", messages[0]);
        Assert.Equal("{\"x\":\n1}", messages[1]); // multi-line data joined by newline
    }

    [Fact]
    public void ExtractResult_returns_the_matching_response()
    {
        string[] messages =
        [
            """{"jsonrpc":"2.0","method":"notifications/message","params":{}}""", // skip: notification
            """{"jsonrpc":"2.0","id":99,"result":{"other":true}}""",              // skip: different id
            """{"jsonrpc":"2.0","id":5,"result":{"ok":true}}""",
        ];

        var result = McpProtocol.ExtractResult(messages, 5);
        Assert.True(result.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void ExtractResult_throws_on_json_rpc_error()
    {
        string[] messages = ["""{"jsonrpc":"2.0","id":5,"error":{"code":-32601,"message":"method not found"}}"""];

        var ex = Assert.Throws<McpException>(() => McpProtocol.ExtractResult(messages, 5));
        Assert.Contains("method not found", ex.Message);
    }

    [Fact]
    public void ExtractResult_throws_when_no_response_matches()
    {
        Assert.Throws<McpException>(() => McpProtocol.ExtractResult(["""{"jsonrpc":"2.0","id":1,"result":{}}"""], 2));
    }

    [Fact]
    public void MapToolResult_concatenates_text_and_reads_flags()
    {
        var result = Json("""
            {"content":[{"type":"text","text":"line 1"},{"type":"image","data":"…"},{"type":"text","text":"line 2"}],
             "structuredContent":{"id":42},"isError":false}
            """);

        var mapped = McpProtocol.MapToolResult(result);

        Assert.Equal("line 1\nline 2", mapped.Text);
        Assert.False(mapped.IsError);
        Assert.Equal(42, mapped.StructuredContent!.Value.GetProperty("id").GetInt32());
    }

    [Fact]
    public void MapToolResult_flags_tool_errors_and_tolerates_missing_content()
    {
        var error = McpProtocol.MapToolResult(Json("""{"content":[{"type":"text","text":"boom"}],"isError":true}"""));
        Assert.True(error.IsError);
        Assert.Equal("boom", error.Text);

        var empty = McpProtocol.MapToolResult(Json("""{}"""));
        Assert.Equal("", empty.Text);
        Assert.False(empty.IsError);
        Assert.Null(empty.StructuredContent);
    }
}
