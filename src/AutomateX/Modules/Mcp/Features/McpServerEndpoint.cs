using System.Text.Json;
using System.Text.Json.Nodes;
using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Modules.ApiKeys;
using AutomateX.Modules.Executions;
using AutomateX.Modules.Workspaces;
using AutomateX.Web;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutomateX.Modules.Mcp.Features;

// The MCP server: a JSON-RPC endpoint that exposes a workspace's opted-in workflows as tools. Off
// unless Engine__AllowMcpServer is true AND an API key is configured (the /api gate validates the key;
// this refuses in open mode so exposure is never accidental). Workspace-scoped via X-Workspace-Id.
public static class McpServer
{
    public sealed class Endpoint(
        AutomateXDbContext dbContext,
        McpWorkflowRunner runner,
        WorkspaceAccess access,
        IOptions<EngineOptions> engineOptions,
        IOptions<AuthOptions> authOptions) : Endpoint<JsonObject>
    {
        public override void Configure()
        {
            Post("mcp");
            AllowAnonymous();
        }

        public override async Task HandleAsync(JsonObject req, CancellationToken ct)
        {
            if (!engineOptions.Value.AllowMcpServer)
            {
                await Send.NotFoundAsync(ct); // server disabled
                return;
            }

            // MCP is never exposed in open dev: it requires the instance to authenticate — OIDC, or (the
            // headline use case) a DB key on this request.
            if (!authOptions.Value.OidcConfigured && ApiKeyContext.From(HttpContext) is null)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Viewer, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            // A Workflow-scoped key exposes/calls exactly its one workflow; any other caller sees the
            // whole workspace's opted-in set.
            var only = WorkspaceAccess.ScopedWorkflowId(HttpContext);

            var method = (string?)req["method"];
            var id = req["id"];

            // JSON-RPC notifications (no id) — e.g. notifications/initialized — get no response.
            if (id is null)
            {
                await Send.NoContentAsync(ct);
                return;
            }

            JsonNode? result = null;
            (int Code, string Message)? error = null;

            switch (method)
            {
                case "initialize":
                    result = Initialize();
                    break;
                case "ping":
                    result = new JsonObject();
                    break;
                case "tools/list":
                    result = await ToolsListAsync(ws, only, ct);
                    break;
                case "tools/call":
                    (result, error) = await ToolsCallAsync(ws, only, req["params"] as JsonObject ?? [], ct);
                    break;
                default:
                    error = (-32601, $"Method not found: {method}");
                    break;
            }

            var response = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone() };
            if (error is { } e)
            {
                response["error"] = new JsonObject { ["code"] = e.Code, ["message"] = e.Message };
            }
            else
            {
                response["result"] = result;
            }

            await Send.OkAsync(response, ct);
        }

        private static JsonObject Initialize() => new()
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
            ["serverInfo"] = new JsonObject { ["name"] = "AutomateX", ["version"] = "4.5.0" },
        };

        private async Task<JsonObject> ToolsListAsync(Guid ws, Guid? only, CancellationToken ct)
        {
            var workflows = await ExposedAsync(ws, only, ct);
            var tools = new JsonArray();
            foreach (var workflow in workflows)
            {
                tools.Add(new JsonObject
                {
                    ["name"] = McpTools.ResolveToolName(workflow.McpToolName, workflow.Name),
                    ["description"] = workflow.McpDescription ?? workflow.Description ?? workflow.Name,
                    ["inputSchema"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["payload"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["description"] = "The trigger payload passed to the workflow.",
                            },
                        },
                    },
                });
            }

            return new JsonObject { ["tools"] = tools };
        }

        private async Task<(JsonNode? Result, (int, string)? Error)> ToolsCallAsync(
            Guid ws, Guid? only, JsonObject parameters, CancellationToken ct)
        {
            var name = (string?)parameters["name"];
            if (string.IsNullOrEmpty(name))
            {
                return (null, (-32602, "Missing tool name."));
            }

            var workflows = await ExposedAsync(ws, only, ct);
            var match = workflows.FirstOrDefault(x => McpTools.ResolveToolName(x.McpToolName, x.Name) == name);
            if (match is null)
            {
                return (null, (-32602, $"Unknown tool: {name}"));
            }

            var payload = (parameters["arguments"] as JsonObject)?["payload"] is { } p ? p.ToJsonString() : null;
            var run = await runner.RunAndWaitAsync(match.Id, $"mcp:{name}", payload, engineOptions.Value.McpCallTimeout, ct);

            if (!run.Settled)
            {
                return (ToolContent($"Workflow started; still running. executionId={run.ExecutionId}", isError: false), null);
            }

            if (run.Status == ExecutionStatus.Failed)
            {
                return (ToolContent($"Workflow failed (executionId={run.ExecutionId}).", isError: true), null);
            }

            var content = McpTools.ResultContent(run.Output);
            var text = content.ValueKind == JsonValueKind.String ? content.GetString() ?? "" : content.GetRawText();
            return (ToolContent(text, isError: false), null);
        }

        private Task<List<WorkflowRow>> ExposedAsync(Guid ws, Guid? only, CancellationToken ct) =>
            dbContext.Workflows
                .AsNoTracking()
                .Where(x => x.WorkspaceId == ws && x.McpExposed && x.Enabled && (only == null || x.Id == only))
                .Select(x => new WorkflowRow(x.Id, x.Name, x.Description, x.McpToolName, x.McpDescription))
                .ToListAsync(ct);

        private static JsonObject ToolContent(string text, bool isError) => new()
        {
            ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } },
            ["isError"] = isError,
        };

        private sealed record WorkflowRow(Guid Id, string Name, string? Description, string? McpToolName, string? McpDescription);
    }
}
