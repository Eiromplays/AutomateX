using AutomateX.Plugin.Sdk;

namespace AutomateX.Plugins.Discord;

[ConnectionType("discord", "Discord", Description = "A Discord channel webhook for posting messages.")]
public sealed class DiscordConnectionType : IConnectionType, IConnectionTester
{
    public IReadOnlyList<ConnectionField> Fields { get; } =
    [
        new("webhookUrl", "Webhook URL",
            HelpText: "Channel Settings → Integrations → Webhooks → New Webhook → Copy Webhook URL.",
            DocsUrl: "https://support.discord.com/hc/en-us/articles/228383668-Intro-to-Webhooks"),
    ];

    // GET on a webhook returns its info (200) without posting anything — non-intrusive.
    public async Task<ConnectionTestResult> TestAsync(
        IReadOnlyDictionary<string, string> values, HttpClient http, CancellationToken cancellationToken)
    {
        if (!values.TryGetValue("webhookUrl", out var url) || string.IsNullOrWhiteSpace(url))
        {
            return new ConnectionTestResult(false, "No webhook URL set.");
        }

        using var response = await http.GetAsync(url, cancellationToken);
        return response.IsSuccessStatusCode
            ? new ConnectionTestResult(true, "Webhook is reachable.")
            : new ConnectionTestResult(false, $"Discord returned HTTP {(int)response.StatusCode}.");
    }
}
