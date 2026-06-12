using System.Text.Json;
using AutomateX.Plugin.Sdk;
using Microsoft.Extensions.Logging;

namespace AutomateX.Engine.Mcp;

// ServerUrl + Token are usually templated from an `mcp` connection
// ({{connections.<name>.serverUrl}} / .token). Arguments is the tool's JSON input object.
public sealed record McpCallConfig(
    string ServerUrl,
    string Tool,
    JsonElement Arguments,
    string? Token = null,
    Dictionary<string, string>? Headers = null);

public sealed record McpCallResult(string Text, JsonElement? StructuredContent);

[Action("mcp.call", "MCP: Call Tool",
    Description = "Calls a tool on an MCP (Model Context Protocol) server and returns its result. "
        + "Point serverUrl/token at an 'MCP server' connection (e.g. {{connections.myserver.serverUrl}}); "
        + "set tool to the tool name and arguments to its JSON input. A tool error fails the step.")]
public sealed class McpCallAction : IAction<McpCallConfig, McpCallResult>
{
    public async Task<McpCallResult> ExecuteAsync(
        McpCallConfig config, ActionContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(config.ServerUrl))
        {
            throw new McpException("No MCP server URL.");
        }

        if (string.IsNullOrWhiteSpace(config.Tool))
        {
            throw new McpException("No tool name.");
        }

        var headers = new Dictionary<string, string>(config.Headers ?? new(), StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(config.Token) && !headers.ContainsKey("Authorization"))
        {
            headers["Authorization"] = $"Bearer {config.Token}";
        }

        var arguments = config.Arguments.ValueKind == JsonValueKind.Object ? config.Arguments : EmptyObject;
        var result = await new McpClient(context.Http)
            .CallToolAsync(new McpServer(config.ServerUrl, headers), config.Tool, arguments, cancellationToken);

        if (result.IsError)
        {
            throw new McpException(result.Text.Length > 0 ? result.Text : "The MCP tool returned an error.");
        }

        context.Logger.LogInformation("mcp.call {Tool} ok", config.Tool);
        return new McpCallResult(result.Text, result.StructuredContent);
    }

    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();
}
