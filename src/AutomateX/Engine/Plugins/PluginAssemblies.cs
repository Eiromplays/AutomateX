using Microsoft.Extensions.Options;

namespace AutomateX.Engine.Plugins;

// A plugin discovered by path only — never loaded into the host. The out-of-proc runtime launches a
// PluginHost per path and describes it, so plugin code never runs in the engine process.
// Convention: plugins/<Name>/<Name>.dll (global) and plugins/.workspaces/<id>/<Name>/<Name>.dll
// (workspace-scoped); dot-prefixed directories are reserved and skipped by the global scan.
public sealed record PluginPath(string Name, string DllPath, Guid? WorkspaceId);

public sealed class PluginAssemblies(
    IOptions<EngineOptions> engineOptions,
    ILogger<PluginAssemblies> logger)
{
    public const string WorkspacesDirName = ".workspaces";

    public string GlobalRoot => ResolveRoot();

    public string WorkspaceRoot(Guid workspaceId) =>
        Path.Combine(ResolveRoot(), WorkspacesDirName, workspaceId.ToString());

    // Plugin dll paths without loading anything in-process.
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

        logger.LogDebug("Discovered {Count} plugin path(s) under {Root}", result.Count, root);
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

    private string ResolveRoot()
    {
        var pluginsPath = engineOptions.Value.PluginsPath;
        return Path.IsPathRooted(pluginsPath)
            ? pluginsPath
            : Path.Combine(AppContext.BaseDirectory, pluginsPath);
    }
}
