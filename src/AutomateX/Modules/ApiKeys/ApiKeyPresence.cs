namespace AutomateX.Modules.ApiKeys;

// Caches "does the instance have any usable key?" so the gate's engagement check isn't a DB round-trip
// per request. Only consulted when neither a static key nor OIDC is configured (the DB-key-only mode).
// Over-engagement is safe (a stale "yes" still forces auth, and a revoked key won't resolve); the only
// unsafe drift is a stale "no", so mint/bootstrap invalidate. Startup begins Unknown, so the first
// request re-queries and picks up any bootstrap key.
public sealed class ApiKeyPresence
{
    private volatile bool _checked;
    private volatile bool _hasKeys;

    public bool? Cached => _checked ? _hasKeys : null;

    public void Remember(bool hasKeys)
    {
        _hasKeys = hasKeys;
        _checked = true;
    }

    public void Invalidate() => _checked = false;
}
