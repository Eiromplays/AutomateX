using AutomateX.Engine.Plugins;
using Microsoft.AspNetCore.SignalR;

namespace AutomateX.Web;

// Bridges appended plugin log lines onto SignalR for the live ops tail. Resolves the dll path back to the
// global plugin name (the map GetPluginStatus.ResolveDll uses in reverse) and broadcasts to that plugin's
// group. Best-effort like the execution bridge — and, since it runs on the plugin read-loop thread, it
// must never throw: a throw there would tear down the plugin connection. The ring + poll are the backstop.
public sealed class SignalRPluginLogSink(
    IHubContext<PluginLogsHub> hubContext, PluginAssemblies plugins, ILogger<SignalRPluginLogSink> logger)
    : IPluginLogSink
{
    public void Publish(string pluginDll, PluginLogLine line)
    {
        try
        {
            if (ResolveName(pluginDll) is not { } name)
            {
                return; // unknown/non-global dll — nothing to route
            }

            _ = hubContext.Clients.Group(PluginLogGroups.ForPlugin(name)).SendAsync("pluginLog", line);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Dropped a plugin log line for {Dll} on the live stream", pluginDll);
        }
    }

    private string? ResolveName(string pluginDll) =>
        plugins.EnumeratePaths().FirstOrDefault(p => p.WorkspaceId is null && p.DllPath == pluginDll)?.Name;
}
