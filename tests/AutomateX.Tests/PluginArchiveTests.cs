using System.IO.Compression;
using AutomateX.Engine.Plugins;
using Xunit;

namespace AutomateX.Tests;

public sealed class PluginArchiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"automatex-plugin-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static MemoryStream Zip(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void Valid_archive_extracts_flat_publish_output()
    {
        PluginArchive.Extract(
            Zip(("My.Plugin.dll", "bin"), ("My.Plugin.deps.json", "{}"), ("runtimes/linux-x64/native/lib.so", "so")),
            "My.Plugin", _root);

        Assert.True(File.Exists(Path.Combine(_root, "My.Plugin", "My.Plugin.dll")));
        Assert.True(File.Exists(Path.Combine(_root, "My.Plugin", "My.Plugin.deps.json")));
        Assert.True(File.Exists(Path.Combine(_root, "My.Plugin", "runtimes", "linux-x64", "native", "lib.so")));
    }

    [Fact]
    public void Re_upload_replaces_the_existing_folder_completely()
    {
        PluginArchive.Extract(Zip(("My.Plugin.dll", "v1"), ("stale.txt", "old")), "My.Plugin", _root);
        PluginArchive.Extract(Zip(("My.Plugin.dll", "v2")), "My.Plugin", _root);

        Assert.Equal("v2", File.ReadAllText(Path.Combine(_root, "My.Plugin", "My.Plugin.dll")));
        Assert.False(File.Exists(Path.Combine(_root, "My.Plugin", "stale.txt")));
    }

    [Fact]
    public void Archive_without_the_plugin_dll_is_rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => PluginArchive.Extract(Zip(("other.dll", "x")), "My.Plugin", _root));

        Assert.Contains("My.Plugin.dll", exception.Message);
        Assert.False(Directory.Exists(Path.Combine(_root, "My.Plugin")));
    }

    [Fact]
    public void Zip_slip_entries_are_rejected_before_anything_is_written()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => PluginArchive.Extract(Zip(("My.Plugin.dll", "x"), ("../evil.txt", "x")), "My.Plugin", _root));

        Assert.Contains("escapes", exception.Message);
        Assert.False(Directory.Exists(Path.Combine(_root, "My.Plugin")));
        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData(".hidden")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void Unsafe_plugin_names_are_rejected(string name)
    {
        Assert.Throws<InvalidOperationException>(
            () => PluginArchive.Extract(Zip(($"{name}.dll", "x")), name, _root));
    }
}
