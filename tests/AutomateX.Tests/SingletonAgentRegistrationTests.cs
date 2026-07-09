using AutomateX.Engine;
using AutomateX.Engine.Plugins;
using AutomateX.Engine.Triggers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Wolverine.Runtime.Agents;
using Xunit;

namespace AutomateX.Tests;

// v5.0 m3: the cluster-singleton workers run on exactly one node. In Balanced they are Wolverine agents
// (leader election picks the node); in Solo they run as plain hosted services on the single node. Either way,
// they are registered ONE way only (never both), and the node-local plugin supervisors are always hosted
// services so they run on every node. Asserted on the service registrations, without booting a host.
public sealed class SingletonAgentRegistrationTests
{
    private static readonly Type[] ClusterSingletons =
    [
        typeof(CronScheduler),
        typeof(PluginTriggerHost),
        typeof(StuckExecutionSweeper),
        typeof(RetentionSweeper),
    ];

    private static (List<Type?> Agents, List<Type?> Hosted) Register(string? mode)
    {
        var builder = Host.CreateApplicationBuilder();
        if (mode is not null)
        {
            builder.Configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Engine:DurabilityMode"] = mode });
        }

        builder.AddAutomateXEngine("Host=localhost;Database=x;Username=x;Password=x");

        var agents = builder.Services
            .Where(d => d.ServiceType == typeof(IAgentFamily))
            .Select(d => d.ImplementationType)
            .ToList();
        var hosted = builder.Services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType)
            .ToList();
        return (agents, hosted);
    }

    [Fact]
    public void Balanced_registers_singletons_as_agents_not_hosted_services()
    {
        var (agents, hosted) = Register("Balanced");

        Assert.All(ClusterSingletons, t => Assert.Contains(t, agents));
        Assert.All(ClusterSingletons, t => Assert.DoesNotContain(t, hosted));
    }

    [Fact]
    public void Solo_registers_singletons_as_hosted_services_not_agents()
    {
        var (agents, hosted) = Register("Solo");

        Assert.All(ClusterSingletons, t => Assert.Contains(t, hosted));
        Assert.All(ClusterSingletons, t => Assert.DoesNotContain(t, agents));
    }

    [Fact]
    public void Node_local_supervisors_are_always_hosted_services()
    {
        foreach (var mode in new[] { "Balanced", "Solo" })
        {
            var (_, hosted) = Register(mode);
            Assert.Contains(typeof(PluginWatcher), hosted);
            Assert.Contains(typeof(PluginMemoryWatchdog), hosted);
        }
    }
}
