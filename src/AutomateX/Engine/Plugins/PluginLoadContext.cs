using System.Reflection;
using System.Runtime.Loader;

namespace AutomateX.Engine.Plugins;

public sealed class PluginLoadContext(string pluginAssemblyPath) : AssemblyLoadContext(
    name: Path.GetFileNameWithoutExtension(pluginAssemblyPath),
    isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new(pluginAssemblyPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // SDK + framework assemblies must unify with the host's copies, otherwise the
        // plugin's IAction<,> is a different Type than the one the engine scans for.
        if (assemblyName.Name is "AutomateX.Plugin.Sdk"
            || assemblyName.Name!.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal)
            || assemblyName.Name.StartsWith("System.", StringComparison.Ordinal))
        {
            return null;
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? base.LoadUnmanagedDll(unmanagedDllName) : LoadUnmanagedDllFromPath(path);
    }
}
