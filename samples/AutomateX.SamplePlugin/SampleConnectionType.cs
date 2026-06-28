using AutomateX.Plugin.Sdk;

namespace AutomateX.SamplePlugin;

// Demonstrates a connection type with both OAuth config + a credential test — the vehicle for the
// out-of-proc connection-protocol tests.
[ConnectionType("sample.conn", "Sample Connection", Description = "Demo connection type.")]
public sealed class SampleConnectionType : IConnectionType, IOAuthConnectionType, IConnectionTester
{
    public IReadOnlyList<ConnectionField> Fields { get; } =
    [
        new("clientId", "Client ID", Secret: false),
        new("clientSecret", "Client secret"),
    ];

    public OAuthConfig BuildOAuthConfig(IReadOnlyDictionary<string, string> values) => new(
        "https://auth.example.com/authorize",
        "https://auth.example.com/token",
        values.GetValueOrDefault("clientId", ""),
        values.GetValueOrDefault("clientSecret", ""),
        ["read"],
        UsePkce: true);

    public Task<ConnectionTestResult> TestAsync(
        IReadOnlyDictionary<string, string> values, HttpClient http, CancellationToken cancellationToken) =>
        Task.FromResult(values.ContainsKey("clientId")
            ? new ConnectionTestResult(true, "ok")
            : new ConnectionTestResult(false, "missing clientId"));
}
