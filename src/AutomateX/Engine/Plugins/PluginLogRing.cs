namespace AutomateX.Engine.Plugins;

public sealed record PluginLogLine(long Seq, DateTimeOffset At, string Level, string? Source, string Message);

// A bounded, thread-safe per-plugin log buffer. Each line gets a monotonic Seq so a poller can ask for
// everything "since" its last cursor; old lines drop once the cap is reached.
public sealed class PluginLogRing
{
    private const int Capacity = 500;

    private readonly Lock _lock = new();
    private readonly LinkedList<PluginLogLine> _lines = new();
    private long _seq;

    public PluginLogLine Add(string level, string? source, string message)
    {
        lock (_lock)
        {
            var line = new PluginLogLine(++_seq, DateTimeOffset.UtcNow, level, source, message);
            _lines.AddLast(line);
            if (_lines.Count > Capacity)
            {
                _lines.RemoveFirst();
            }

            return line;
        }
    }

    public IReadOnlyList<PluginLogLine> Since(long cursor)
    {
        lock (_lock)
        {
            return _lines.Where(x => x.Seq > cursor).ToList();
        }
    }
}
