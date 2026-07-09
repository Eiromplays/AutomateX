using AutomateX.Web;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// v5.0 m5: the SignalR Redis backplane is opt-in. With ConnectionStrings:redis set, the hub lifetime manager
// is swapped for the Redis one (cross-node fan-out); unset, it stays the in-process default (single node).
// Registration only — AddStackExchangeRedis connects lazily, so no Redis is needed to assert the wiring.
public sealed class SignalRBackplaneTests
{
    private static ServiceDescriptor HubLifetimeManager(string? redisConnectionString)
    {
        var services = new ServiceCollection();
        SignalRExtensions.AddSignalRWithBackplane(services, redisConnectionString);
        // The backplane appends its manager after the default; the last registration wins on resolve.
        return services.Last(d => d.ServiceType == typeof(HubLifetimeManager<>));
    }

    [Fact]
    public void Redis_connection_string_enables_the_backplane()
    {
        var descriptor = HubLifetimeManager("localhost:6379");
        Assert.Contains("StackExchangeRedis", descriptor.ImplementationType!.FullName!);
    }

    [Fact]
    public void No_redis_keeps_the_in_process_default()
    {
        var descriptor = HubLifetimeManager(redisConnectionString: null);
        Assert.DoesNotContain("StackExchangeRedis", descriptor.ImplementationType?.FullName ?? "");
    }
}
