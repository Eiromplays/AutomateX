namespace AutomateX.Engine.Plugins;

// Semantic-version precedence (semver.org §11) for "which version is newest" and "is an update available".
// Inputs are assumed valid (x.y.z core) — catalog versions and manifests are regex-validated on parse.
// Build metadata is ignored; a pre-release sorts below its release.
public static class SemVer
{
    public static int Compare(string a, string b)
    {
        var (aCore, aPre) = Split(a);
        var (bCore, bPre) = Split(b);

        for (var i = 0; i < 3; i++)
        {
            var core = aCore[i].CompareTo(bCore[i]);
            if (core != 0)
            {
                return core;
            }
        }

        if (aPre.Length == 0 && bPre.Length == 0)
        {
            return 0;
        }

        // A version with a pre-release has lower precedence than the same core without one.
        if (aPre.Length == 0)
        {
            return 1;
        }

        if (bPre.Length == 0)
        {
            return -1;
        }

        return ComparePreRelease(aPre, bPre);
    }

    private static (int[] Core, string[] PreRelease) Split(string version)
    {
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            version = version[..plus];
        }

        var dash = version.IndexOf('-', StringComparison.Ordinal);
        var core = (dash >= 0 ? version[..dash] : version).Split('.');
        var preRelease = dash >= 0 ? version[(dash + 1)..].Split('.') : [];

        return ([int.Parse(core[0]), int.Parse(core[1]), int.Parse(core[2])], preRelease);
    }

    private static int ComparePreRelease(string[] a, string[] b)
    {
        var shared = Math.Min(a.Length, b.Length);
        for (var i = 0; i < shared; i++)
        {
            var aNumeric = int.TryParse(a[i], out var aValue);
            var bNumeric = int.TryParse(b[i], out var bValue);

            int result;
            if (aNumeric && bNumeric)
            {
                result = aValue.CompareTo(bValue);
            }
            else if (aNumeric)
            {
                result = -1; // numeric identifiers rank below alphanumeric
            }
            else if (bNumeric)
            {
                result = 1;
            }
            else
            {
                result = string.CompareOrdinal(a[i], b[i]);
            }

            if (result != 0)
            {
                return result;
            }
        }

        // All shared identifiers equal — the longer set has higher precedence.
        return a.Length.CompareTo(b.Length);
    }
}
