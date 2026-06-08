using System.Reflection;
using AutomateX.Plugin.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace AutomateX.Engine.Connections;

public sealed record ConnectionTypeDescriptor(
    string Type,
    string DisplayName,
    string? Description,
    string Source,
    IReadOnlyList<ConnectionField> Fields);

public interface IConnectionTypeSource
{
    IEnumerable<ConnectionTypeDescriptor> GetConnectionTypes();
}

public static class ConnectionTypeDiscovery
{
    public static IEnumerable<ConnectionTypeDescriptor> FromAssembly(Assembly assembly, string source, IServiceProvider services)
    {
        foreach (var type in assembly.GetTypes())
        {
            var attribute = type.GetCustomAttribute<ConnectionTypeAttribute>();
            if (attribute is null || type.IsAbstract || !typeof(IConnectionType).IsAssignableFrom(type))
            {
                continue;
            }

            var instance = (IConnectionType)ActivatorUtilities.CreateInstance(services, type);
            yield return new ConnectionTypeDescriptor(
                attribute.Type, attribute.DisplayName, attribute.Description, source, instance.Fields);
        }
    }
}
