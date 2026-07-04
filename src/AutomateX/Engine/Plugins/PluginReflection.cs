using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AutomateX.Engine.Plugins;

// Type-discovery resilience for the built-in sources. Assembly.GetTypes() throws
// ReflectionTypeLoadException if ANY type fails to load (typically a missing dependency), which would
// drop the other, working types too. Keep the ones that loaded and log the rest.
//
// Since v4.0 external plugins run out-of-process (discovered by describing their host), so this now
// only runs over the trusted first-party assembly for BuiltInActionSource / BuiltInConnectionTypeSource
// and the test trigger source — where a partial load shouldn't happen, but the guard is cheap to keep.
public static class PluginReflection
{
    public static IEnumerable<Type> LoadableTypes(Assembly assembly, IServiceProvider services, string source)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            var loadable = Loadable(ex);
            var logger = services.GetService<ILoggerFactory>()?.CreateLogger("AutomateX.Engine.Plugins.Discovery")
                ?? (ILogger)NullLogger.Instance;
            logger.LogWarning(ex,
                "Some types in {Source} failed to load (a missing dependency?); discovering the {Count} that loaded",
                source, loadable.Length);
            return loadable;
        }
    }

    public static Type[] Loadable(ReflectionTypeLoadException exception) =>
        exception.Types.OfType<Type>().ToArray();
}
