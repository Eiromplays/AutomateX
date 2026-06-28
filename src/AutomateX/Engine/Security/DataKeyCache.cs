using System.Collections.Concurrent;

namespace AutomateX.Engine.Security;

// Process-wide cache of unwrapped DEKs — the only place a data-encryption key lives unwrapped (in
// memory). Singleton; invalidated when a workspace's keys rotate.
public sealed class DataKeyCache
{
    private readonly ConcurrentDictionary<(Guid WorkspaceId, int Version), byte[]> _keys = new();

    public bool TryGet(Guid workspaceId, int version, out byte[] dek) =>
        _keys.TryGetValue((workspaceId, version), out dek!);

    public void Set(Guid workspaceId, int version, byte[] dek) => _keys[(workspaceId, version)] = dek;

    public void Invalidate(Guid workspaceId)
    {
        foreach (var key in _keys.Keys.Where(k => k.WorkspaceId == workspaceId).ToArray())
        {
            _keys.TryRemove(key, out _);
        }
    }
}
