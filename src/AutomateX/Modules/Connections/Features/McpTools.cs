using System.Text.Json;
using AutomateX.Database;
using AutomateX.Engine.Mcp;
using AutomateX.Engine.Security;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Connections.Features;

// Lists an MCP server connection's tools (live tools/list call) so the builder can offer a
// tool dropdown + a guided arguments form from each tool's JSON-Schema inputSchema.
public static class McpTools
{
    public sealed class Endpoint(
        AutomateXDbContext dbContext,
        SecretCipher cipher,
        IHttpClientFactory httpClientFactory,
        WorkspaceAccess access) : EndpointWithoutRequest<List<Response>>
    {
        public override void Configure()
        {
            Post("connections/{id}/mcp/tools");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Editor, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var id = Route<Guid>("id");
            var connection = await dbContext.Connections
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id && x.WorkspaceId == ws, ct);

            if (connection is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            Dictionary<string, string> values;
            try
            {
                values = JsonSerializer.Deserialize<Dictionary<string, string>>(cipher.Decrypt(connection.EncryptedSecrets)) ?? [];
            }
            catch (SecretCipherException ex)
            {
                ThrowError(ex.Message);
                return;
            }

            var serverUrl = values.GetValueOrDefault("serverUrl");
            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                ThrowError("This connection has no server URL.");
                return;
            }

            Dictionary<string, string> headers = [];
            if (values.TryGetValue("token", out var token) && !string.IsNullOrEmpty(token))
            {
                headers["Authorization"] = $"Bearer {token}";
            }

            try
            {
                var tools = await new McpClient(httpClientFactory.CreateClient())
                    .ListToolsAsync(new McpServer(serverUrl, headers), ct);
                await Send.OkAsync(
                    tools.Select(t => new Response(
                        t.Name,
                        t.Description,
                        t.InputSchema.ValueKind == JsonValueKind.Undefined ? "{}" : t.InputSchema.GetRawText()))
                    .ToList(),
                    ct);
            }
            catch (McpException ex)
            {
                ThrowError(ex.Message);
            }
        }
    }

    public sealed record Response(string Name, string? Description, string InputSchema);
}
