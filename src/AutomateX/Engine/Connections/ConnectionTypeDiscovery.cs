using System.Reflection;
using AutomateX.Engine.Plugins;
using AutomateX.Plugin.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace AutomateX.Engine.Connections;

public sealed record ConnectionTypeDescriptor(
    string Type,
    string DisplayName,
    string? Description,
    string Source,
    IReadOnlyList<ConnectionField> Fields);

// The descriptor (for the UI) plus the live instance (for testing credentials).
public sealed record RegisteredConnectionType(ConnectionTypeDescriptor Descriptor, IConnectionType Instance);

public interface IConnectionTypeSource
{
    IEnumerable<RegisteredConnectionType> GetConnectionTypes();
}

public static class ConnectionTypeDiscovery
{
    public static IEnumerable<RegisteredConnectionType> FromAssembly(Assembly assembly, string source, IServiceProvider services)
    {
        foreach (var type in PluginReflection.LoadableTypes(assembly, services, source))
        {
            var attribute = type.GetCustomAttribute<ConnectionTypeAttribute>();
            if (attribute is null || type.IsAbstract || !typeof(IConnectionType).IsAssignableFrom(type))
            {
                continue;
            }

            var instance = (IConnectionType)ActivatorUtilities.CreateInstance(services, type);
            yield return new RegisteredConnectionType(
                new ConnectionTypeDescriptor(attribute.Type, attribute.DisplayName, attribute.Description, source, instance.Fields),
                instance);
        }
    }
}
