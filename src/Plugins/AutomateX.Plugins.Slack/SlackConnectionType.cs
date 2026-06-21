using AutomateX.Plugin.Sdk;

namespace AutomateX.Plugins.Slack;

[ConnectionType("slack", "Slack", Description = "A Slack incoming webhook for posting messages to a channel.")]
public sealed class SlackConnectionType : IConnectionType, IConnectionTester
{
    public IReadOnlyList<ConnectionField> Fields { get; } =
    [
        new("webhookUrl", "Webhook URL",
            HelpText: "Create an incoming webhook at api.slack.com/apps → Incoming Webhooks → Add New Webhook.",
            DocsUrl: "https://api.slack.com/messaging/webhooks"),
    ];

    // Slack incoming webhooks have no non-intrusive probe (a GET 404s, any POST delivers a message),
    // so the test only confirms the URL looks like a Slack webhook rather than calling out.
    public Task<ConnectionTestResult> TestAsync(
        IReadOnlyDictionary<string, string> values, HttpClient http, CancellationToken cancellationToken)
    {
        if (!values.TryGetValue("webhookUrl", out var url) || string.IsNullOrWhiteSpace(url))
        {
            return Task.FromResult(new ConnectionTestResult(false, "No webhook URL set."));
        }

        var ok = Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host.EndsWith("slack.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith("/services/", StringComparison.Ordinal);

        return Task.FromResult(ok
            ? new ConnectionTestResult(true, "Looks like a valid Slack webhook URL.")
            : new ConnectionTestResult(false, "Not a Slack incoming webhook URL (expected https://hooks.slack.com/services/…)."));
    }
}
