using System.Net.Http.Headers;
using AutomateX.Plugin.Sdk;

namespace AutomateX.Plugins.Matrix;

[ConnectionType("matrix", "Matrix", Description = "A Matrix bot account for sending and receiving messages.")]
public sealed class MatrixConnectionType : IConnectionType, IConnectionTester
{
    public IReadOnlyList<ConnectionField> Fields { get; } =
    [
        new("homeserverUrl", "Homeserver URL", Secret: false,
            HelpText: "e.g. https://matrix-client.matrix.org"),
        new("accessToken", "Access token",
            HelpText: "A bot account's access token — get one via the login API (see the recipe).",
            DocsUrl: "https://github.com/Eiromplays/AutomateX/blob/main/docs/recipes/jarvis-lite.md"),
    ];

    // whoami confirms the access token without side effects.
    public async Task<ConnectionTestResult> TestAsync(
        IReadOnlyDictionary<string, string> values, HttpClient http, CancellationToken cancellationToken)
    {
        if (!values.TryGetValue("homeserverUrl", out var homeserver) || !values.TryGetValue("accessToken", out var token)
            || string.IsNullOrWhiteSpace(homeserver) || string.IsNullOrWhiteSpace(token))
        {
            return new ConnectionTestResult(false, "Missing homeserver URL or access token.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{homeserver.TrimEnd('/')}/_matrix/client/v3/account/whoami");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode
            ? new ConnectionTestResult(true, "Access token is valid.")
            : new ConnectionTestResult(false, $"Matrix returned HTTP {(int)response.StatusCode}.");
    }
}
