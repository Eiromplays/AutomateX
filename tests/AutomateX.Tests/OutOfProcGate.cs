using System.Diagnostics.CodeAnalysis;
using Xunit;
using Xunit.Abstractions;

namespace AutomateX.Tests;

// Out-of-proc tests need the PluginHost + a plugin dll built. A clean local checkout may not have them
// yet, so they skip. CI sets AUTOMATEX_REQUIRE_OOP=1 to turn that skip into a failure — a silently
// skipped sandbox test would be a false green for the whole isolation model.
internal static class OutOfProcGate
{
    public static bool Ready(
        [NotNullWhen(true)] string? hostDll,
        [NotNullWhen(true)] string? pluginDll,
        ITestOutputHelper output)
    {
        if (hostDll is not null && pluginDll is not null)
        {
            return true;
        }

        Assert.False(
            Environment.GetEnvironmentVariable("AUTOMATEX_REQUIRE_OOP") == "1",
            "AUTOMATEX_REQUIRE_OOP is set but the PluginHost or plugin binaries were not found — build the solution.");
        output.WriteLine("Skipped — build the solution first.");
        return false;
    }
}
