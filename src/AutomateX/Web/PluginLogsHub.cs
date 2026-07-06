using AutomateX.Modules.Workspaces;
using Microsoft.AspNetCore.SignalR;

namespace AutomateX.Web;

public static class PluginLogGroups
{
    // One SignalR group per global plugin — lines route here; only instance admins join.
    public static string ForPlugin(string name) => $"plugin:{name}";
}

// A dedicated, instance-admin-only hub for the live plugin-log tail. The auth boundary differs from
// ExecutionEventsHub (workspace membership): plugin logs are a global ops view that can carry sensitive
// runtime data, so JoinPlugin mirrors GetPluginLogs — instance-admin only. A non-admin is silently
// declined (no group add), so no lines ever reach it.
public sealed class PluginLogsHub(WorkspaceAccess access) : Hub
{
    public async Task JoinPlugin(string name)
    {
        // The handshake HttpContext carries the ApiKeyContext/principal the gate set at negotiate time.
        if (Context.GetHttpContext() is not { } http || !access.IsInstanceAdmin(http))
        {
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, PluginLogGroups.ForPlugin(name));
    }
}
