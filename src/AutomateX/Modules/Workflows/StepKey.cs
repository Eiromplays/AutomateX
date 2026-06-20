using System.Text;

namespace AutomateX.Modules.Workflows;

// Derives stable reference slugs for steps from their display name, with positional
// fallback. Uniqueness within a version is the caller's job via Unique.
public static class StepKey
{
    public static string Slugify(string? name, int order)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return $"step-{order + 1}";
        }

        var slug = new StringBuilder(name.Length);
        var pendingDash = false;
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                if (pendingDash && slug.Length > 0)
                {
                    slug.Append('-');
                }

                slug.Append(ch);
                pendingDash = false;
            }
            else
            {
                pendingDash = true;
            }
        }

        return slug.Length == 0 ? $"step-{order + 1}" : slug.ToString();
    }

    // Assigns the final, unique key for every step in a version, in order. Mirrors what
    // WorkflowVersion.Create persists, so callers (validation) can reason about keys pre-save.
    public static IReadOnlyList<string> AssignAll(IReadOnlyList<StepDefinition> steps)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var keys = new string[steps.Count];
        for (var order = 0; order < steps.Count; order++)
        {
            var definition = steps[order];
            var baseKey = Slugify(
                string.IsNullOrWhiteSpace(definition.Key) ? definition.Name : definition.Key, order);
            keys[order] = Unique(baseKey, taken);
        }

        return keys;
    }

    // Returns baseKey if free, else baseKey-2, baseKey-3, … Adds the result to taken.
    public static string Unique(string baseKey, ISet<string> taken)
    {
        if (taken.Add(baseKey))
        {
            return baseKey;
        }

        for (var n = 2; ; n++)
        {
            var candidate = $"{baseKey}-{n}";
            if (taken.Add(candidate))
            {
                return candidate;
            }
        }
    }
}
