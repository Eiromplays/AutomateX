using AutomateX.Database;
using AutomateX.Modules.Mcp;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Workflows.Features;

// Expose (or unexpose) a workflow as an MCP tool. Workflow-level metadata, so it's a small dedicated
// endpoint rather than part of the versioned builder save.
public static class SetWorkflowMcp
{
    public sealed record Request(bool Exposed, string? ToolName, string? Description);

    public sealed record Response(bool Exposed, string ResolvedToolName);

    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access, Audit.IAuditSink audit)
        : Endpoint<Request, Response>
    {
        public override void Configure()
        {
            Put("workflows/{id}/mcp");
            AllowAnonymous();
        }

        public override async Task HandleAsync(Request req, CancellationToken ct)
        {
            var id = Route<Guid>("id");
            if (await access.AuthorizeWorkflowAsync(HttpContext, id, WorkspaceRole.Editor, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var workflow = await dbContext.Workflows.FirstOrDefaultAsync(x => x.Id == id && x.WorkspaceId == ws, ct);
            if (workflow is null)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            var toolName = req.Exposed ? McpTools.ResolveToolName(req.ToolName, workflow.Name) : null;

            // Tool names must be unique per workspace so tools/list and tools/call are unambiguous.
            if (req.Exposed && await dbContext.Workflows.AnyAsync(
                x => x.WorkspaceId == ws && x.Id != id && x.McpExposed
                    && (x.McpToolName ?? "") == toolName, ct))
            {
                ThrowError($"Another workflow already exposes the MCP tool '{toolName}'.");
            }

            workflow.SetMcp(req.Exposed, toolName, req.Exposed ? req.Description : null);
            await dbContext.SaveChangesAsync(ct);

            await audit.RecordAsync(
                req.Exposed ? "workflow.mcp.expose" : "workflow.mcp.unexpose",
                ws, WorkspaceAccess.GetActor(HttpContext), "workflow", id.ToString(), workflow.Name, ct);

            await Send.OkAsync(new Response(req.Exposed, toolName ?? ""), ct);
        }
    }
}
