using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AutomateX.Engine.Plugins;

// Plugin type discovery resilience. Assembly.GetTypes() throws ReflectionTypeLoadException
// if ANY type fails to load (typically a missing dependency), which would drop a plugin's
// other, working types too. Keep the ones that loaded and log the rest.
//
// This is an in-process workaround: out-of-proc plugin sandboxing (v3.* roadmap) retires
// it, since a broken plugin would fail in isolation in its own process.
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
