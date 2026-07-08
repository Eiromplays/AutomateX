using AutomateX.Database;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.ApiKeys;

// Turns a presented atx_ token into an ApiKeyContext (or null). Lookup is O(1) on the unique Prefix
// index; the secret is compared in fixed time; revoked/expired keys are rejected. LastUsedAt is stamped
// at most once per UsageStampInterval so a hot key doesn't cause a write per request.
public sealed class ApiKeyResolver(AutomateXDbContext dbContext, ApiKeyPresence presence, TimeProvider timeProvider)
{
    private static readonly TimeSpan UsageStampInterval = TimeSpan.FromMinutes(1);

    public async Task<ApiKeyContext?> ResolveAsync(string token, CancellationToken cancellationToken)
    {
        if (!ApiKeyToken.TryParse(token, out var prefix, out var presentedHash))
        {
            return null;
        }

        var key = await dbContext.ApiKeys.FirstOrDefaultAsync(x => x.Prefix == prefix, cancellationToken);
        if (key is null || !ApiKeyToken.Verify(presentedHash, key.SecretHash))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        if (!key.IsActive(now))
        {
            return null;
        }

        presence.Remember(true);

        if (key.LastUsedAt is null || now - key.LastUsedAt.Value > UsageStampInterval)
        {
            key.MarkUsed(now);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new ApiKeyContext(
            key.Scope, key.WorkspaceId, key.WorkflowId, key.Role, $"key:{key.Name}",
            key.RateLimitPerMinute, ClientIpAllowlist.Parse(key.IpAllowlist));
    }

    // Whether the instance has any usable key — the gate's signal to engage in DB-key-only mode.
    public async Task<bool> HasActiveKeysAsync(CancellationToken cancellationToken)
    {
        if (presence.Cached is { } cached)
        {
            return cached;
        }

        var now = timeProvider.GetUtcNow();
        var any = await dbContext.ApiKeys
            .AnyAsync(x => x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now), cancellationToken);
        presence.Remember(any);
        return any;
    }
}
