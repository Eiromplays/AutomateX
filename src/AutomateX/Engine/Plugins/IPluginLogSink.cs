namespace AutomateX.Engine.Plugins;

// Best-effort fan-out for plugin log lines as they're appended, keyed by plugin dll path so the engine
// stays name-agnostic. The engine wires the no-op default; the Web layer overrides it to broadcast over
// SignalR. Non-reliable by design — the bounded PluginLogRing remains the snapshot/backstop.
public interface IPluginLogSink
{
    void Publish(string pluginDll, PluginLogLine line);
}

public sealed class NullPluginLogSink : IPluginLogSink
{
    public static NullPluginLogSink Instance { get; } = new();

    public void Publish(string pluginDll, PluginLogLine line)
    {
    }
}
