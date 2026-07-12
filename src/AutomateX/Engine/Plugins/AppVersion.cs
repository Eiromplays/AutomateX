using System.Reflection;

namespace AutomateX.Engine.Plugins;

public static class AppVersion
{
    // The running app's semver, or null if it can't be read as one — used for a plugin's
    // minAutomateXVersion compatibility check. Build metadata is dropped; a valid numeric core is required
    // so the value is always safe to hand to SemVer.Compare.
    public static string? Current { get; } = Read();

    private static string? Read()
    {
        var raw = typeof(AppVersion).Assembly
                      .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                  ?? typeof(AppVersion).Assembly.GetName().Version?.ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var version = raw.Split('+')[0];
        var core = version.Split('-')[0].Split('.');
        return core.Length >= 3
               && int.TryParse(core[0], out _)
               && int.TryParse(core[1], out _)
               && int.TryParse(core[2], out _)
            ? version
            : null;
    }
}
