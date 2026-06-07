using System.Collections.Frozen;
using System.Reflection;
using AutomateX.Engine.Plugins;
using AutomateX.Plugin.Sdk;

namespace AutomateX.Engine.Events;

// In-process, best-effort event dispatch: published after state is persisted, exact
// event-type matching, and per-listener fault isolation — a throwing listener is
// logged and skipped, never allowed to affect engine flow.
// Listeners come from DI and GLOBAL plugins only: engine events are instance-wide,
// so a workspace plugin listener would observe other workspaces' activity.
// Workspace plugins contribute actions, never listeners.
public sealed class EngineEventBus
{
    private sealed record Subscription(object Listener, MethodInfo Handle, string Name);

    private readonly IReadOnlyList<IEngineEventListener> _hostListeners;
    private readonly PluginAssemblies _plugins;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EngineEventBus> _logger;
    private volatile FrozenDictionary<Type, Subscription[]> _subscriptions;

    public EngineEventBus(
        IEnumerable<IEngineEventListener> listeners,
        PluginAssemblies pluginAssemblies,
        IServiceProvider serviceProvider,
        ILogger<EngineEventBus> logger)
    {
        _hostListeners = [.. listeners];
        _plugins = pluginAssemblies;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _subscriptions = Build();
    }

    public void Rebuild() => _subscriptions = Build();

    private FrozenDictionary<Type, Subscription[]> Build()
    {
        List<object> all = [.. _hostListeners];
        foreach (var plugin in _plugins.Current.Global)
        {
            try
            {
                all.AddRange(plugin.Assembly.GetTypes()
                    .Where(type => type is { IsClass: true, IsAbstract: false }
                        && typeof(IEngineEventListener).IsAssignableFrom(type))
                    .Select(type => ActivatorUtilities.CreateInstance(_serviceProvider, type)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to discover event listeners in plugin {Plugin}", plugin.Name);
            }
        }

        return all
            .SelectMany(listener => listener.GetType().GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IListenFor<>))
                .Select(i => (
                    EventType: i.GenericTypeArguments[0],
                    Subscription: new Subscription(
                        listener,
                        i.GetMethod(nameof(IListenFor<IEngineEvent>.HandleAsync))!,
                        listener.GetType().Name))))
            .GroupBy(x => x.EventType)
            .ToFrozenDictionary(g => g.Key, g => g.Select(x => x.Subscription).ToArray());
    }

    public async Task PublishAsync(IEngineEvent engineEvent, CancellationToken cancellationToken = default)
    {
        if (!_subscriptions.TryGetValue(engineEvent.GetType(), out var subscriptions))
        {
            return;
        }

        foreach (var subscription in subscriptions)
        {
            try
            {
                await (Task)subscription.Handle.Invoke(subscription.Listener, [engineEvent, cancellationToken])!;
            }
            catch (Exception ex)
            {
                var error = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
                _logger.LogError(error,
                    "Event listener {Listener} failed handling {Event}; engine flow unaffected",
                    subscription.Name, engineEvent.GetType().Name);
            }
        }
    }
}
