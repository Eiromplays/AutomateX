using System.Threading.RateLimiting;
using AutomateX.Modules.ApiKeys;
using Microsoft.AspNetCore.RateLimiting;

namespace AutomateX.Web;

public static class RateLimitPolicies
{
    public const string Webhook = "webhook";
    public const string Auth = "auth";
}

// Defense-in-depth rate limiting for the public, unauthenticated endpoints (webhook fire and the
// API-key session exchange). Partitioned per client IP via the connection's RemoteIpAddress —
// behind the reverse proxy, forwarded-headers processing (trusted proxies only) resolves that to
// the real client, and an untrusted caller can't rotate it the way a raw X-Forwarded-For header
// could. Not the primary control (webhook secrets / the API key are), just a brake on abuse.
public static class RateLimitingExtensions
{
    public static IServiceCollection AddAutomateXRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.AddPolicy(RateLimitPolicies.Webhook, context => Partition(context, RateLimitPolicies.Webhook, permitLimit: 60));
            options.AddPolicy(RateLimitPolicies.Auth, context => Partition(context, RateLimitPolicies.Auth, permitLimit: 10));

            // Applies to every request: an authenticated key with a per-minute limit is bucketed by its
            // own id; everything else (no key, no limit) passes through untouched.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                KeyRateLimit(context) is { } limit
                    ? RateLimitPartition.GetFixedWindowLimiter(
                        limit.PartitionKey,
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = limit.PermitLimit,
                            Window = TimeSpan.FromMinutes(1),
                        })
                    : RateLimitPartition.GetNoLimiter<string>("unlimited"));
        });

        return services;
    }

    // The per-key rate-limit partition for an authenticated key that carries a limit, or null to pass
    // through. Keyed by the key's unique id — Actor/Name isn't unique, so two keys never share a bucket.
    public static (string PartitionKey, int PermitLimit)? KeyRateLimit(HttpContext context) =>
        ApiKeyContext.From(context) is { RateLimitPerMinute: { } permitLimit } key
            ? ($"key:{key.KeyId}", permitLimit)
            : null;

    private static RateLimitPartition<string> Partition(HttpContext context, string policy, int permitLimit)
    {
        var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            $"{policy}:{client}",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = TimeSpan.FromMinutes(1) });
    }
}
