using System.Reflection;
using AutomateX.Engine.Plugins;
using AutomateX.Plugin.Protocol;
using AutomateX.Plugin.Sdk;

namespace AutomateX.Engine.Actions;

public static class ActionDiscovery
{
    public static IEnumerable<RegisteredAction> FromAssembly(Assembly assembly, string source, IServiceProvider services)
    {
        var contextFactory = services.GetRequiredService<ActionContextFactory>();

        foreach (var type in PluginReflection.LoadableTypes(assembly, services, source))
        {
            var attribute = type.GetCustomAttribute<ActionAttribute>();
            if (attribute is null || type.IsAbstract)
            {
                continue;
            }

            var contract = type.GetInterfaces().FirstOrDefault(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAction<,>))
                ?? throw new InvalidOperationException(
                    $"[Action] type {type.FullName} must implement IAction<TConfig, TResult>.");

            var configType = contract.GenericTypeArguments[0];
            var resultType = contract.GenericTypeArguments[1];

            var instance = ActivatorUtilities.CreateInstance(services, type);
            var executor = (IActionExecutor)Activator.CreateInstance(
                typeof(SdkActionExecutor<,>).MakeGenericType(configType, resultType),
                instance, attribute.Type, contextFactory)!;

            yield return new RegisteredAction(
                new ActionDescriptor(
                    attribute.Type,
                    attribute.DisplayName,
                    attribute.Description,
                    source,
                    SchemaExport.ForType(configType),
                    SchemaExport.ForType(resultType)),
                executor);
        }
    }
}
