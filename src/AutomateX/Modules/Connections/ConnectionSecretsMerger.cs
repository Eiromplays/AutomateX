namespace AutomateX.Modules.Connections;

// Merge semantics (recorded decision): provided keys overwrite, null deletes,
// absent keys stay untouched — rotate one field without re-entering the rest.
public static class ConnectionSecretsMerger
{
    public static Dictionary<string, string> Merge(
        IReadOnlyDictionary<string, string> existing,
        IReadOnlyDictionary<string, string?> patch)
    {
        var result = existing.ToDictionary(x => x.Key, x => x.Value);
        foreach (var (key, value) in patch)
        {
            if (value is null)
            {
                result.Remove(key);
            }
            else
            {
                result[key] = value;
            }
        }

        return result;
    }
}
