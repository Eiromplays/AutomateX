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
        // The SDK and the shared Microsoft.Extensions.* contracts must unify with the
        // host's copies — never load a plugin-local version, or the plugin's IAction<,>
        // / ILogger become foreign Types the engine can't match.
        if (assemblyName.Name is "AutomateX.Plugin.Sdk"
            || assemblyName.Name!.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal))
        {
            return null;
        }

        // Let the resolver decide for everything else: bundled deps (SSH.NET,
        // System.ServiceModel.Syndication, …) resolve to a path in the plugin folder and
        // load there; true framework assemblies (System.Runtime, System.Text.Json, …)
        // aren't deployed alongside, so the resolver returns null and they fall through
        // to the host — unity preserved without an over-broad "System.*" name rule that
        // also swallowed out-of-band System.* packages the host doesn't ship.
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? base.LoadUnmanagedDll(unmanagedDllName) : LoadUnmanagedDllFromPath(path);
    }
}
