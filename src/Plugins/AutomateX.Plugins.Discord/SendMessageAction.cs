using System.Net.Http.Json;
using AutomateX.Plugin.Sdk;

namespace AutomateX.Plugins.Discord;

public sealed record DiscordSendConfig(
    string WebhookUrl,
    [property: Multiline] string Content,
    string? Username = null);

public sealed record DiscordSendResult(int StatusCode);

[Action("discord.send", "Discord: Send Message",
    Description = "Posts a message to a Discord channel webhook (use {{connections.<name>.webhookUrl}}). "
        + "Optional username overrides the webhook's display name.")]
public sealed class SendMessageAction : IAction<DiscordSendConfig, DiscordSendResult>
{
    public async Task<DiscordSendResult> ExecuteAsync(
        DiscordSendConfig config,
        ActionContext context,
        CancellationToken cancellationToken = default)
    {
        Validate(config);

        var payload = new Dictionary<string, string> { ["content"] = config.Content };
        if (config.Username is { Length: > 0 })
        {
            payload["username"] = config.Username;
        }

        using var response = await context.Http.PostAsync(
            config.WebhookUrl, JsonContent.Create(payload), cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"discord.send failed: {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }

        return new DiscordSendResult((int)response.StatusCode);
    }

    private static void Validate(DiscordSendConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.WebhookUrl))
        {
            throw new ArgumentException("discord.send requires 'webhookUrl'.");
        }

        if (string.IsNullOrWhiteSpace(config.Content))
        {
            throw new ArgumentException("discord.send requires 'content'.");
        }
    }
}
