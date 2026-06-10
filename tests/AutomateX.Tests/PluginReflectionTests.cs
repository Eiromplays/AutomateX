using System.Reflection;
using AutomateX.Engine.Plugins;
using Xunit;

namespace AutomateX.Tests;

// Assembly.GetTypes() is all-or-nothing: one type with a missing dependency throws and
// would lose a plugin's other, perfectly loadable types. Loadable keeps what loaded.
// (Out-of-proc sandboxing will retire this — a broken plugin fails in its own process.)
public sealed class PluginReflectionTests
{
    [Fact]
    public void Loadable_keeps_loaded_types_and_drops_the_failures()
    {
        var exception = new ReflectionTypeLoadException(
            classes: [typeof(string), null, typeof(int)],
            exceptions: [null, new TypeLoadException("missing dependency"), null]);

        var loadable = PluginReflection.Loadable(exception);

        Assert.Equal(new[] { typeof(string), typeof(int) }, loadable);
    }

    [Fact]
    public void Loadable_is_empty_when_nothing_loaded()
    {
        var exception = new ReflectionTypeLoadException(
            classes: [null, null],
            exceptions: [new TypeLoadException("a"), new TypeLoadException("b")]);

        Assert.Empty(PluginReflection.Loadable(exception));
    }
}
