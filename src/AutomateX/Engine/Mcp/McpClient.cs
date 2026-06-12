using System.Text;
using System.Text.Json;

namespace AutomateX.Engine.Mcp;

// An MCP server endpoint plus the headers to send (auth, etc.).
public sealed record McpServer(string Url, IReadOnlyDictionary<string, string> Headers);

// A tool advertised by tools/list — name, description and its JSON-Schema input shape.
public sealed record McpTool(string Name, string? Description, JsonElement InputSchema);

// Streamable-HTTP MCP client: per call it runs initialize → notifications/initialized →
// the request, threading the server's session header through. Stateless across calls, which
// suits the one-shot nature of a workflow step.
public sealed class McpClient(HttpClient http)
{
    private const string ClientName = "AutomateX";
    private const string ClientVersion = "3.0";

    public async Task<McpToolResult> CallToolAsync(
        McpServer server, string tool, JsonElement arguments, CancellationToken cancellationToken)
    {
        var sessionId = await InitializeAsync(server, cancellationToken);
        var result = await RequestAsync(server, sessionId, id: 2, "tools/call",
            McpProtocol.ToolCallParams(tool, arguments), cancellationToken);
        return McpProtocol.MapToolResult(result);
    }

    public async Task<IReadOnlyList<McpTool>> ListToolsAsync(McpServer server, CancellationToken cancellationToken)
    {
        var sessionId = await InitializeAsync(server, cancellationToken);
        var result = await RequestAsync(server, sessionId, id: 2, "tools/list", null, cancellationToken);

        List<McpTool> tools = [];
        if (result.TryGetProperty("tools", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var tool in array.EnumerateArray())
            {
                tools.Add(new McpTool(
                    tool.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    tool.TryGetProperty("description", out var description) ? description.GetString() : null,
                    tool.TryGetProperty("inputSchema", out var schema) ? schema.Clone() : default));
            }
        }

        return tools;
    }

    private async Task<string?> InitializeAsync(McpServer server, CancellationToken cancellationToken)
    {
        var (messages, sessionId) = await SendAsync(
            server, McpProtocol.BuildRequest(1, "initialize", McpProtocol.InitializeParams(ClientName, ClientVersion)),
            sessionId: null, cancellationToken);
        McpProtocol.ExtractResult(messages, 1); // throws on a handshake error
        await SendNotificationAsync(
            server, sessionId, McpProtocol.BuildNotification("notifications/initialized", null), cancellationToken);
        return sessionId;
    }

    private async Task<JsonElement> RequestAsync(
        McpServer server, string? sessionId, int id, string method, object? @params, CancellationToken cancellationToken)
    {
        var (messages, _) = await SendAsync(server, McpProtocol.BuildRequest(id, method, @params), sessionId, cancellationToken);
        return McpProtocol.ExtractResult(messages, id);
    }

    private async Task SendNotificationAsync(McpServer server, string? sessionId, string body, CancellationToken cancellationToken)
    {
        using var request = BuildHttpRequest(server, body, sessionId);
        using var response = await SendCoreAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new McpException($"MCP server returned {(int)response.StatusCode} on notifications/initialized.");
        }
    }

    private async Task<(List<string> Messages, string? SessionId)> SendAsync(
        McpServer server, string body, string? sessionId, CancellationToken cancellationToken)
    {
        using var request = BuildHttpRequest(server, body, sessionId);
        using var response = await SendCoreAsync(request, cancellationToken);

        var returnedSession = response.Headers.TryGetValues("Mcp-Session-Id", out var values)
            ? values.FirstOrDefault() ?? sessionId
            : sessionId;

        if (!response.IsSuccessStatusCode)
        {
            throw new McpException($"MCP server returned {(int)response.StatusCode}.");
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        List<string> messages = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
            ? [.. McpProtocol.ParseSseMessages(content)]
            : [content];

        return (messages, returnedSession);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new McpException($"Could not reach the MCP server: {ex.Message}");
        }
    }

    private static HttpRequestMessage BuildHttpRequest(McpServer server, string body, string? sessionId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, server.Url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", McpProtocol.ProtocolVersion);
        if (sessionId is not null)
        {
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
        }

        foreach (var (key, value) in server.Headers)
        {
            request.Headers.TryAddWithoutValidation(key, value);
        }

        return request;
    }
}
