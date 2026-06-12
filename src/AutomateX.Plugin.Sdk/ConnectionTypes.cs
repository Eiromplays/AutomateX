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

// The OAuth2 parameters for a connection, built from its stored field values. The generic
// type reads endpoints + client from the values; presets hardcode endpoints/scopes and read
// only the client id/secret.
public sealed record OAuthConfig(
    string AuthorizationEndpoint,
    string TokenEndpoint,
    string ClientId,
    string ClientSecret,
    IReadOnlyList<string> Scopes,
    bool UsePkce);

// Optional: a connection type whose credential is obtained via the OAuth2 authorization-code
// flow. The host runs the consent redirect, code exchange, and refresh; this just maps the
// connection's stored values to the OAuth parameters.
public interface IOAuthConnectionType : IConnectionType
{
    OAuthConfig BuildOAuthConfig(IReadOnlyDictionary<string, string> values);
}

public sealed record ConnectionTestResult(bool Ok, string Message);

// Optional: a connection type can verify its credentials work (a webhook ping, an
// auth call, an SMTP connect). `values` are the connection's decrypted field values.
public interface IConnectionTester
{
    Task<ConnectionTestResult> TestAsync(
        IReadOnlyDictionary<string, string> values, HttpClient http, CancellationToken cancellationToken);
}
