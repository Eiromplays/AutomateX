using AutomateX.Plugin.Sdk;

namespace AutomateX.Modules.Connections;

// Generic OAuth2 connection: the user registers their own app with any provider and supplies
// the endpoints + client credentials here, then runs the consent flow with "Connect". The
// access/refresh tokens are written back into the same encrypted blob after consent.
[ConnectionType("oauth2", "OAuth 2.0 (generic)",
    Description = "Connect to any OAuth2 provider via the authorization-code flow. Register an app with the "
        + "provider, set its redirect URI to <your-host>/api/connections/oauth/callback, then paste the client "
        + "id/secret and endpoints here and click Connect.")]
public sealed class OAuth2ConnectionType : IConnectionType, IOAuthConnectionType
{
    public IReadOnlyList<ConnectionField> Fields { get; } =
    [
        new("clientId", "Client ID", Secret: false, HelpText: "From your OAuth app registration."),
        new("clientSecret", "Client secret", Required: false,
            HelpText: "From your OAuth app. Leave blank for public (PKCE-only) clients."),
        new("authorizeUrl", "Authorization endpoint", Secret: false,
            HelpText: "e.g. https://github.com/login/oauth/authorize"),
        new("tokenUrl", "Token endpoint", Secret: false,
            HelpText: "e.g. https://github.com/login/oauth/access_token"),
        new("scopes", "Scopes", Secret: false, Required: false,
            HelpText: "Space-separated, e.g. repo read:user"),
    ];

    public OAuthConfig BuildOAuthConfig(IReadOnlyDictionary<string, string> values) => new(
        AuthorizationEndpoint: values.GetValueOrDefault("authorizeUrl", ""),
        TokenEndpoint: values.GetValueOrDefault("tokenUrl", ""),
        ClientId: values.GetValueOrDefault("clientId", ""),
        ClientSecret: values.GetValueOrDefault("clientSecret", ""),
        Scopes: (values.GetValueOrDefault("scopes") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        UsePkce: true);
}
