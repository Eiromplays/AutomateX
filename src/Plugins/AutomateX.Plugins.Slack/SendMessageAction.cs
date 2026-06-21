using System.Net.Http.Json;
using AutomateX.Plugin.Sdk;

namespace AutomateX.Plugins.Slack;

public sealed record SlackSendConfig(
    string WebhookUrl,
    [property: Multiline] string Text,
    string? Username = null,
    string? IconEmoji = null);

public sealed record SlackSendResult(int StatusCode);

[Action("slack.send", "Slack: Send Message",
    Description = "Posts a message to a Slack incoming webhook (use {{connections.<name>.webhookUrl}}). "
        + "Optional username and icon emoji (e.g. :robot_face:) override the webhook defaults.")]
public sealed class SendMessageAction : IAction<SlackSendConfig, SlackSendResult>
{
    public async Task<SlackSendResult> ExecuteAsync(
        SlackSendConfig config,
        ActionContext context,
        CancellationToken cancellationToken = default)
    {
        Validate(config);

        var payload = new Dictionary<string, string> { ["text"] = config.Text };
        if (config.Username is { Length: > 0 })
        {
            payload["username"] = config.Username;
        }

        if (config.IconEmoji is { Length: > 0 })
        {
            payload["icon_emoji"] = config.IconEmoji;
        }

        using var response = await context.Http.PostAsync(
            config.WebhookUrl, JsonContent.Create(payload), cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"slack.send failed: {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }

        return new SlackSendResult((int)response.StatusCode);
    }

    private static void Validate(SlackSendConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.WebhookUrl))
        {
            throw new ArgumentException("slack.send requires 'webhookUrl'.");
        }

        if (string.IsNullOrWhiteSpace(config.Text))
        {
            throw new ArgumentException("slack.send requires 'text'.");
        }
    }
}
