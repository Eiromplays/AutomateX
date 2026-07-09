using AutomateX.Engine.Plugins;
using AutomateX.Plugin.Sdk;

namespace AutomateX.Web;

public static class SignalRExtensions
{
    // Realtime wiring: the two hubs plus their engine bridges. In multi-node deploys an optional Redis
    // backplane fans hub messages out across nodes; without it, everything stays in-process (single node).
    public static WebApplicationBuilder AddAutomateXRealtime(this WebApplicationBuilder builder)
    {
        AddSignalRWithBackplane(builder.Services, builder.Configuration.GetConnectionString("redis"));

        builder.Services.AddSingleton<IEngineEventListener, SignalRExecutionEventListener>();
        // Overrides the engine's NullPluginLogSink: fan plugin log lines out over SignalR for the live tail.
        builder.Services.AddSingleton<IPluginLogSink, SignalRPluginLogSink>();
        return builder;
    }

    // Opt-in Redis backplane: set only when ConnectionStrings:redis is configured. Redis is the only
    // first-party self-hosted SignalR backplane; unset = today's in-process, single-node behaviour.
    internal static void AddSignalRWithBackplane(IServiceCollection services, string? redisConnectionString)
    {
        var signalR = services.AddSignalR();
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            signalR.AddStackExchangeRedis(redisConnectionString);
        }
    }
}
