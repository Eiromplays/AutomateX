namespace AutomateX.Modules.Variables;

// One variable's already-decrypted values across environments. IsWorkflowScope marks a workflow
// override (vs a workspace variable); Secret drives masking once resolved.
public sealed record VariableValueSet(
    string Name,
    bool IsWorkflowScope,
    bool Secret,
    IReadOnlyDictionary<Guid, string> ValuesByEnvironment);

// Pure resolution: collapse the workspace + workflow variables into a single name → value map for one
// environment. Workflow scope shadows workspace scope on a name collision; a variable with no value
// for the chosen environment falls back to the default environment's value; a variable with neither is
// simply absent (a {{vars.x}} ref to it is then unresolved). Returns the value map plus the set of
// names whose winning variable is secret, for masking.
public static class VariableResolution
{
    public static (IReadOnlyDictionary<string, string> Values, IReadOnlySet<string> SecretNames) Resolve(
        IEnumerable<VariableValueSet> variables, Guid environmentId, Guid defaultEnvironmentId)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        HashSet<string> secrets = new(StringComparer.Ordinal);

        // Workspace (false) before workflow (true) — OrderBy is stable, so workflow overwrites.
        foreach (var variable in variables.OrderBy(x => x.IsWorkflowScope))
        {
            if (!TryPick(variable, environmentId, defaultEnvironmentId, out var value))
            {
                continue;
            }

            values[variable.Name] = value;
            if (variable.Secret)
            {
                secrets.Add(variable.Name);
            }
            else
            {
                secrets.Remove(variable.Name); // a plain workflow override de-secrets a shadowed secret
            }
        }

        return (values, secrets);
    }

    private static bool TryPick(VariableValueSet variable, Guid environmentId, Guid defaultEnvironmentId, out string value)
    {
        if (variable.ValuesByEnvironment.TryGetValue(environmentId, out var direct))
        {
            value = direct;
            return true;
        }

        if (variable.ValuesByEnvironment.TryGetValue(defaultEnvironmentId, out var fallback))
        {
            value = fallback;
            return true;
        }

        value = "";
        return false;
    }
}
