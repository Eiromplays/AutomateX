using AutomateX.Modules.Workspaces;
using Microsoft.AspNetCore.SignalR;

namespace AutomateX.Web;

public static class ExecutionEventGroups
{
    // One SignalR group per workspace — events route here, only members join.
    public static string ForWorkspace(Guid workspaceId) => $"workspace:{workspaceId}";
}

// Clients call JoinWorkspace to subscribe to their current workspace's live events.
// Membership is validated against WorkspaceAccess, so a connection can only receive
// events for workspaces it's allowed to read (open/apikey modes = everyone, as elsewhere).
public sealed class ExecutionEventsHub(WorkspaceAccess access) : Hub
{
    public async Task JoinWorkspace(string workspaceId)
    {
        if (!Guid.TryParse(workspaceId, out var id))
        {
            return;
        }

        // The connection's HttpContext carries the ApiKeyContext the gate set at handshake, so a scoped
        // key can only join its own workspace's group.
        if (Context.GetHttpContext() is not { } http
            || await access.GetRoleAsync(id, http, Context.ConnectionAborted) is null)
        {
            return; // not a member — silently decline; no events will reach this connection
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, ExecutionEventGroups.ForWorkspace(id));
    }
}
