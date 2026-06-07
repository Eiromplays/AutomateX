using System.IO.Compression;

namespace AutomateX.Engine.Plugins;

// Upload format: a zip of the flat `dotnet publish` output, named <PluginName>.zip,
// containing <PluginName>.dll at its root. Every entry is validated against zip-slip
// before anything touches disk.
public static class PluginArchive
{
    public static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.StartsWith('.')
            || name.Contains("..", StringComparison.Ordinal)
            || name.Contains('/')
            || name.Contains('\\')
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException($"'{name}' is not a valid plugin name.");
        }
    }

    public static string Extract(Stream zipStream, string pluginName, string pluginsRoot)
    {
        ValidateName(pluginName);

        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        if (!archive.Entries.Any(x => string.Equals(x.FullName, $"{pluginName}.dll", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"The archive must contain {pluginName}.dll at its root (zip the flat 'dotnet publish' output).");
        }

        var targetDir = Path.GetFullPath(Path.Combine(pluginsRoot, pluginName));
        foreach (var entry in archive.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));
            if (!destination.StartsWith(targetDir + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Archive entry '{entry.FullName}' escapes the plugin folder.");
            }
        }

        if (Directory.Exists(targetDir))
        {
            Directory.Delete(targetDir, recursive: true);
        }

        Directory.CreateDirectory(targetDir);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/'))
            {
                continue;
            }

            var destination = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }

        return targetDir;
    }
}
