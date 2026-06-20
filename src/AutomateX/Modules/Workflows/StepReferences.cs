using System.Text.RegularExpressions;

namespace AutomateX.Modules.Workflows;

// Validates {{steps.<id>.output…}} references in step configs against the version's steps.
// <id> is either a numeric order or a step key. A reference that can never resolve — an
// out-of-range order or an unknown key — is a hard error rejected at save. Reachability
// (referencing a step that runs later) is left to the builder, like the trigger-entry footgun.
public static partial class StepReferences
{
    [GeneratedRegex(@"\{\{\s*steps\.([^.}\s]+)\.output[^}]*\}\}")]
    private static partial Regex StepRef();

    public static void Validate(IReadOnlyList<StepDefinition> steps, Action<string> fail)
    {
        var keys = new HashSet<string>(StepKey.AssignAll(steps), StringComparer.Ordinal);

        foreach (var definition in steps)
        {
            foreach (Match match in StepRef().Matches(definition.ConfigJson))
            {
                var id = match.Groups[1].Value;
                if (int.TryParse(id, out var order))
                {
                    if (order < 0 || order >= steps.Count)
                    {
                        fail($"Step reference {{{{steps.{id}.output…}}}} points at a step that doesn't exist.");
                    }
                }
                else if (!keys.Contains(id))
                {
                    fail($"Step reference {{{{steps.{id}.output…}}}} names a step that doesn't exist.");
                }
            }
        }
    }
}
