using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Options;

namespace AutomateX.Engine.Plugins;

public sealed record PluginAssembly(string Name, Assembly Assembly, AssemblyLoadContext LoadContext);

// A plugin discovered by path only (out-of-proc mode): never loaded into the host.
public sealed record PluginPath(string Name, string DllPath, Guid? WorkspaceId);

public sealed class PluginSnapshot(
    IReadOnlyList<PluginAssembly> global,
    IReadOnlyDictionary<Guid, IReadOnlyList<PluginAssembly>> workspaces)
{
    public IReadOnlyList<PluginAssembly> Global { get; } = global;

    public IReadOnlyDictionary<Guid, IReadOnlyList<PluginAssembly>> Workspaces { get; } = workspaces;
}

// Loads plugin folders into collectible ALCs. Convention: plugins/<Name>/<Name>.dll
// (global) and plugins/.workspaces/<workspaceId>/<Name>/<Name>.dll (workspace-scoped);
// dot-prefixed directories are reserved and skipped by the global scan.
// Reload swaps the snapshot and unloads old contexts — in-flight executions keep
// references into the old ALC and drain safely before it collects.
public sealed class PluginAssemblies(
    IOptions<EngineOptions> engineOptions,
    ILogger<PluginAssemblies> logger)
{
    public const string WorkspacesDirName = ".workspaces";

    private readonly Lock _lock = new();
    private PluginSnapshot? _current;

    public PluginSnapshot Current
    {
        get
        {
            lock (_lock)
            {
                return _current ??= Load();
            }
        }
    }

    public string GlobalRoot => ResolveRoot();

    public string WorkspaceRoot(Guid workspaceId) =>
        Path.Combine(ResolveRoot(), WorkspacesDirName, workspaceId.ToString());

    public void Reload()
    {
        lock (_lock)
        {
            var old = _current;
            _current = Load();

            if (old is null)
            {
                return;
            }

            foreach (var plugin in old.Global.Concat(old.Workspaces.Values.SelectMany(x => x)))
            {
                try
                {
                    plugin.LoadContext.Unload();
                    // Best-effort shadow cleanup — may fail while the image is still
                    // mapped; leftovers live under the OS temp dir and get reaped there.
                    Directory.Delete(Path.GetDirectoryName(plugin.Assembly.Location)!, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to unload plugin context {Plugin}", plugin.Name);
                }
            }
        }
    }

    // Plugin dll paths without loading anything in-process — the out-of-proc runtime launches a host
    // per path and describes it, so plugin code never runs in the engine.
    public IReadOnlyList<PluginPath> EnumeratePaths()
    {
        var root = ResolveRoot();
        List<PluginPath> result = [.. ScanPaths(root, null)];

        var workspacesRoot = Path.Combine(root, WorkspacesDirName);
        if (Directory.Exists(workspacesRoot))
        {
            foreach (var directory in Directory.EnumerateDirectories(workspacesRoot))
            {
                if (Guid.TryParse(Path.GetFileName(directory), out var workspaceId))
                {
                    result.AddRange(ScanPaths(directory, workspaceId));
                }
            }
        }

        return result;
    }

    private static IEnumerable<PluginPath> ScanPaths(string path, Guid? workspaceId)
    {
        if (!Directory.Exists(path))
        {
            yield break;
        }

        foreach (var directory in Directory.EnumerateDirectories(path))
        {
            var name = Path.GetFileName(directory);
            if (name.StartsWith('.'))
            {
                continue; // reserved (e.g. .workspaces)
            }

            var dll = Path.Combine(directory, $"{name}.dll");
            if (File.Exists(dll))
            {
                yield return new PluginPath(name, dll, workspaceId);
            }
        }
    }

    private PluginSnapshot Load()
    {
        var root = ResolveRoot();
        var global = LoadDirectory(root);

        Dictionary<Guid, IReadOnlyList<PluginAssembly>> workspaces = [];
        var workspacesRoot = Path.Combine(root, WorkspacesDirName);
        if (Directory.Exists(workspacesRoot))
        {
            foreach (var directory in Directory.EnumerateDirectories(workspacesRoot))
            {
                if (!Guid.TryParse(Path.GetFileName(directory), out var workspaceId))
                {
                    continue;
                }

                var plugins = LoadDirectory(directory);
                if (plugins.Count > 0)
                {
                    workspaces[workspaceId] = plugins;
                }
            }
        }

        return new PluginSnapshot(global, workspaces);
    }

    private List<PluginAssembly> LoadDirectory(string path)
    {
        List<PluginAssembly> result = [];

        if (!Directory.Exists(path))
        {
            return result;
        }

        foreach (var directory in Directory.EnumerateDirectories(path))
        {
            var name = Path.GetFileName(directory);
            if (name.StartsWith('.'))
            {
                continue; // reserved (e.g. .workspaces)
            }

            var assemblyPath = Path.Combine(directory, $"{name}.dll");
            if (!File.Exists(assemblyPath))
            {
                logger.LogWarning("Plugin folder {Plugin} contains no {Assembly}, skipping", name, $"{name}.dll");
                continue;
            }

            try
            {
                // Shadow copy: the runtime caches PE images by absolute path, so loading
                // a replaced DLL from its original path can serve stale bytes (and locks
                // the file on Windows). A unique path per load sidesteps both.
                var shadowDir = Path.Combine(
                    Path.GetTempPath(), "automatex-plugins", $"{Guid.CreateVersion7():N}-{name}");
                CopyDirectory(directory, shadowDir);

                var shadowAssemblyPath = Path.Combine(shadowDir, $"{name}.dll");
                var loadContext = new PluginLoadContext(shadowAssemblyPath);
                result.Add(new PluginAssembly(name, loadContext.LoadFromAssemblyPath(shadowAssemblyPath), loadContext));
                logger.LogInformation("Loaded plugin assembly {Plugin}", name);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load plugin {Plugin} from {Path}", name, assemblyPath);
            }
        }

        return result;
    }

    private string ResolveRoot()
    {
        var pluginsPath = engineOptions.Value.PluginsPath;
        return Path.IsPathRooted(pluginsPath)
            ? pluginsPath
            : Path.Combine(AppContext.BaseDirectory, pluginsPath);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
