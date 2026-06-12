using System.Text.Json;
using System.Text.Json.Nodes;
using AutomateX.Engine.Mcp;

namespace AutomateX.Engine.Agent;

// A model's request to invoke a tool. ArgumentsJson is the raw JSON string the model emits
// (OpenAI sends tool-call arguments as a string), parsed before the MCP call.
public sealed record ToolCall(string Id, string Name, string ArgumentsJson);

// One turn of the loop: either the model asked for tools (with the verbatim assistant message
// to append before the tool results), or it produced a final answer.
public sealed record AgentTurn(string? FinalContent, IReadOnlyList<ToolCall> ToolCalls, JsonNode? AssistantMessage);

// Pure agent <-> OpenAI-tools plumbing: convert MCP tools to the chat-completions `tools`
// schema, parse an assistant turn, and build a tool-result message. No HTTP/state.
public static class AgentProtocol
{
    public static JsonArray ToOpenAiTools(IEnumerable<McpTool> tools)
    {
        var array = new JsonArray();
        foreach (var tool in tools)
        {
            JsonNode parameters = tool.InputSchema.ValueKind == JsonValueKind.Undefined
                ? new JsonObject { ["type"] = "object" }
                : JsonNode.Parse(tool.InputSchema.GetRawText()) ?? new JsonObject { ["type"] = "object" };

            var function = new JsonObject { ["name"] = tool.Name, ["parameters"] = parameters };
            if (tool.Description is not null)
            {
                function["description"] = tool.Description;
            }

            array.Add(new JsonObject { ["type"] = "function", ["function"] = function });
        }

        return array;
    }

    public static AgentTurn ParseTurn(string completionJson)
    {
        using var doc = JsonDocument.Parse(completionJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("The completion had no choices.");
        }

        var message = choices[0].GetProperty("message");

        if (message.TryGetProperty("tool_calls", out var toolCalls)
            && toolCalls.ValueKind == JsonValueKind.Array
            && toolCalls.GetArrayLength() > 0)
        {
            List<ToolCall> calls = [];
            foreach (var call in toolCalls.EnumerateArray())
            {
                var function = call.GetProperty("function");
                calls.Add(new ToolCall(
                    call.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    function.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    function.TryGetProperty("arguments", out var args) ? args.GetString() ?? "{}" : "{}"));
            }

            return new AgentTurn(null, calls, JsonNode.Parse(message.GetRawText()));
        }

        var content = message.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
        return new AgentTurn(content, [], null);
    }

    public static JsonObject ToolResultMessage(string toolCallId, string content) => new()
    {
        ["role"] = "tool",
        ["tool_call_id"] = toolCallId,
        ["content"] = content,
    };
}
