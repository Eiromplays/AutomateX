namespace AutomateX.Plugin.Sdk;

// Declares a named connection shape so the UI can render a guided form (labels,
// help, where-to-get-it links) instead of a bare key/value editor. The connection's
// `provider` field carries the type key. Stored values stay write-only + encrypted;
// Secret just drives the input (password vs text) and masking intent.
[AttributeUsage(AttributeTargets.Class)]
public sealed class ConnectionTypeAttribute(string type, string displayName) : Attribute
{
    public string Type { get; } = type;

    public string DisplayName { get; } = displayName;

    public string? Description { get; init; }
}

public sealed record ConnectionField(
    string Key,
    string Label,
    bool Secret = true,
    bool Required = true,
    string? HelpText = null,
    string? DocsUrl = null);

public interface IConnectionType
{
    IReadOnlyList<ConnectionField> Fields { get; }
}
