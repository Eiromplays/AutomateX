using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutomateX.Engine.Actions;
using AutomateX.Engine.Plugins;
using AutomateX.Plugin.Sdk;

namespace AutomateX.Engine.Triggers;

public sealed record TriggerDescriptor(
    string Type,
    string DisplayName,
    string? Description,
    string Source,
    JsonNode? ConfigSchema);

// Engine-facing handle around a trigger row's listener run.
public sealed record TriggerRunnerContext(Guid TriggerId, Guid WorkflowId, Func<string?, Task> Fire);

public interface ITriggerRunner
{
    string TriggerType { get; }

    Task RunAsync(string configJson, TriggerRunnerContext context, CancellationToken cancellationToken);
}

// CreateRunner is a factory: every (re)start of a listener gets a fresh instance.
public sealed record RegisteredTrigger(TriggerDescriptor Descriptor, Func<ITriggerRunner> CreateRunner);

public interface ITriggerSource
{
    IEnumerable<RegisteredTrigger> GetTriggers();
}

public static class TriggerDiscovery
{
    public static IEnumerable<RegisteredTrigger> FromAssembly(Assembly assembly, string source, IServiceProvider services)
    {
        var contextFactory = services.GetRequiredService<ActionContextFactory>();

        foreach (var type in PluginReflection.LoadableTypes(assembly, services, source))
        {
            var attribute = type.GetCustomAttribute<TriggerAttribute>();
            if (attribute is null || type.IsAbstract)
            {
                continue;
            }

            var contract = type.GetInterfaces().FirstOrDefault(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ITriggerListener<>))
                ?? throw new InvalidOperationException(
                    $"[Trigger] type {type.FullName} must implement ITriggerListener<TConfig>.");

            var configType = contract.GenericTypeArguments[0];
            var listenerType = type;
            var triggerType = attribute.Type;

            yield return new RegisteredTrigger(
                new TriggerDescriptor(
                    triggerType,
                    attribute.DisplayName,
                    attribute.Description,
                    source,
                    SchemaExport.ForType(configType)),
                () => (ITriggerRunner)Activator.CreateInstance(
                    typeof(SdkTriggerRunner<>).MakeGenericType(configType),
                    ActivatorUtilities.CreateInstance(services, listenerType),
                    triggerType,
                    contextFactory)!);
        }
    }
}

public sealed class SdkTriggerRunner<TConfig>(
    object listener,
    string triggerType,
    ActionContextFactory contextFactory) : ITriggerRunner
{
    public string TriggerType => triggerType;

    public Task RunAsync(string configJson, TriggerRunnerContext context, CancellationToken cancellationToken)
    {
        var config = JsonSerializer.Deserialize<TConfig>(configJson, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException($"Invalid config for trigger '{triggerType}'.");

        var sdkContext = contextFactory.CreateTriggerContext(triggerType, context);
        return ((ITriggerListener<TConfig>)listener).RunAsync(config, sdkContext, cancellationToken);
    }
}
