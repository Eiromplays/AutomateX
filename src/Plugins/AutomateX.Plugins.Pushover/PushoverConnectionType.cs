using AutomateX.Plugin.Sdk;

namespace AutomateX.Plugins.Pushover;

[ConnectionType("pushover", "Pushover", Description = "Pushover application + user key for mobile push notifications.")]
public sealed class PushoverConnectionType : IConnectionType, IConnectionTester
{
    public IReadOnlyList<ConnectionField> Fields { get; } =
    [
        new("appToken", "Application token",
            HelpText: "Register an application at pushover.net/apps/build to get its API token.",
            DocsUrl: "https://pushover.net/api"),
        new("userKey", "User or group key",
            HelpText: "Your user key from the Pushover dashboard (or a delivery group key)."),
    ];

    // Pushover's validate endpoint confirms token + user without sending a notification.
    public async Task<ConnectionTestResult> TestAsync(
        IReadOnlyDictionary<string, string> values, HttpClient http, CancellationToken cancellationToken)
    {
        if (!values.TryGetValue("appToken", out var token) || !values.TryGetValue("userKey", out var user)
            || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(user))
        {
            return new ConnectionTestResult(false, "Missing app token or user key.");
        }

        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token, ["user"] = user });
        using var response = await http.PostAsync("https://api.pushover.net/1/users/validate.json", content, cancellationToken);
        return response.IsSuccessStatusCode
            ? new ConnectionTestResult(true, "Token and user key are valid.")
            : new ConnectionTestResult(false, $"Pushover rejected the credentials (HTTP {(int)response.StatusCode}).");
    }
}
