using AutomateX.Modules.ApiKeys;
using AutomateX.Modules.Workspaces;
using AutomateX.Web;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AutomateX.Tests;

// v4.11: the global limiter rate-limits an authenticated key by its own id at its configured limit, and
// passes everything else through. KeyRateLimit is the partition decision the GlobalLimiter is built on.
public sealed class RateLimitingExtensionsTests
{
    private static HttpContext WithKey(ApiKeyContext? key)
    {
        var context = new DefaultHttpContext();
        if (key is not null)
        {
            ApiKeyContext.Set(context, key);
        }

        return context;
    }

    private static ApiKeyContext Key(Guid id, int? rateLimit) =>
        new(ApiKeyScope.Instance, null, null, WorkspaceRole.Owner, "key:x", rateLimit, default, id);

    [Fact]
    public void Keyed_request_with_a_limit_partitions_by_key_id()
    {
        var id = Guid.NewGuid();
        var partition = RateLimitingExtensions.KeyRateLimit(WithKey(Key(id, 30)));

        Assert.NotNull(partition);
        Assert.Equal($"key:{id}", partition!.Value.PartitionKey);
        Assert.Equal(30, partition.Value.PermitLimit);
    }

    [Fact]
    public void Key_without_a_limit_passes_through()
    {
        Assert.Null(RateLimitingExtensions.KeyRateLimit(WithKey(Key(Guid.NewGuid(), null))));
    }

    [Fact]
    public void Unauthenticated_request_passes_through()
    {
        Assert.Null(RateLimitingExtensions.KeyRateLimit(WithKey(null)));
    }

    [Fact]
    public void Two_keys_get_separate_buckets()
    {
        var a = RateLimitingExtensions.KeyRateLimit(WithKey(Key(Guid.NewGuid(), 10)));
        var b = RateLimitingExtensions.KeyRateLimit(WithKey(Key(Guid.NewGuid(), 10)));

        Assert.NotEqual(a!.Value.PartitionKey, b!.Value.PartitionKey);
    }
}
