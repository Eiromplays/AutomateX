using System.Text;
using System.Text.Json;

namespace AutomateX.Engine.Mcp;

public sealed class McpException(string message) : Exception(message);

// A tools/call outcome: the concatenated text content, the optional structured payload, and
// whether the server flagged it an error (which the action turns into a step failure).
public sealed record McpToolResult(string Text, bool IsError, JsonElement? StructuredContent);

// Pure MCP (JSON-RPC 2.0) helpers — message framing, SSE parsing, response extraction and
// tools/call result mapping. No HTTP or state; the client orchestrates these.
public static class McpProtocol
{
    // The MCP spec revision we advertise on initialize; servers negotiate down if needed.
    public const string ProtocolVersion = "2025-06-18";

    public static string BuildRequest(int id, string method, object? @params)
    {
        var message = new Dictionary<string, object?> { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };
        if (@params is not null)
        {
            message["params"] = @params;
        }

        return JsonSerializer.Serialize(message);
    }

    public static string BuildNotification(string method, object? @params)
    {
        var message = new Dictionary<string, object?> { ["jsonrpc"] = "2.0", ["method"] = method };
        if (@params is not null)
        {
            message["params"] = @params;
        }

        return JsonSerializer.Serialize(message);
    }

    public static Dictionary<string, object?> InitializeParams(string clientName, string clientVersion) => new()
    {
        ["protocolVersion"] = ProtocolVersion,
        ["capabilities"] = new Dictionary<string, object?>(),
        ["clientInfo"] = new Dictionary<string, object?> { ["name"] = clientName, ["version"] = clientVersion },
    };

    public static Dictionary<string, object?> ToolCallParams(string name, JsonElement arguments) => new()
    {
        ["name"] = name,
        ["arguments"] = arguments,
    };

    // A Streamable-HTTP response may be a single JSON object or an SSE stream; this yields each
    // event's `data` payload (joining multi-line data, skipping comments and other SSE fields).
    public static IEnumerable<string> ParseSseMessages(string body)
    {
        List<string> dataLines = [];
        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                if (dataLines.Count > 0)
                {
                    yield return string.Join('\n', dataLines);
                    dataLines.Clear();
                }

                continue;
            }

            if (line.StartsWith(':'))
            {
                continue; // SSE comment
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                dataLines.Add(line[5..].TrimStart(' '));
            }
        }

        if (dataLines.Count > 0)
        {
            yield return string.Join('\n', dataLines);
        }
    }

    // Find the JSON-RPC response with our `id` among the messages, returning its `result`.
    // A JSON-RPC error for that id, or no matching response, throws McpException.
    public static JsonElement ExtractResult(IEnumerable<string> messages, int id)
    {
        foreach (var raw in messages)
        {
            JsonElement root;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                root = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                continue;
            }

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("id", out var idElement)
                || !idElement.TryGetInt32(out var messageId)
                || messageId != id)
            {
                continue; // a notification, a server request, or a different response
            }

            if (root.TryGetProperty("error", out var error))
            {
                var code = error.TryGetProperty("code", out var c) && c.TryGetInt32(out var ci) ? ci : 0;
                var message = error.TryGetProperty("message", out var m) ? m.GetString() : "unknown error";
                throw new McpException($"MCP error {code}: {message}");
            }

            if (root.TryGetProperty("result", out var result))
            {
                return result;
            }

            throw new McpException("MCP response had neither result nor error.");
        }

        throw new McpException($"No MCP response for request {id}.");
    }

    public static McpToolResult MapToolResult(JsonElement result)
    {
        var isError = result.TryGetProperty("isError", out var errorFlag) && errorFlag.ValueKind == JsonValueKind.True;

        var text = new StringBuilder();
        if (result.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) && type.GetString() == "text"
                    && block.TryGetProperty("text", out var blockText))
                {
                    if (text.Length > 0)
                    {
                        text.Append('\n');
                    }

                    text.Append(blockText.GetString());
                }
            }
        }

        JsonElement? structured = result.TryGetProperty("structuredContent", out var sc) ? sc.Clone() : null;
        return new McpToolResult(text.ToString(), isError, structured);
    }
}
