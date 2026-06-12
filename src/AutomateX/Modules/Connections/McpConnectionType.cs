using AutomateX.Plugin.Sdk;

namespace AutomateX.Modules.Connections;

// An MCP server stored once and referenced from mcp.call steps via templates
// ({{connections.<name>.serverUrl}} / .token). OAuth-backed servers come in a later phase.
[ConnectionType("mcp", "MCP server",
    Description = "A Model Context Protocol server. Reference it from an 'MCP: Call Tool' step to invoke its tools.")]
public sealed class McpConnectionType : IConnectionType
{
    public IReadOnlyList<ConnectionField> Fields { get; } =
    [
        new("serverUrl", "Server URL", Secret: false,
            HelpText: "The server's Streamable-HTTP endpoint, e.g. https://server.example/mcp"),
        new("token", "Bearer token", Required: false,
            HelpText: "Sent as Authorization: Bearer …. Leave blank for unauthenticated servers."),
    ];
}
