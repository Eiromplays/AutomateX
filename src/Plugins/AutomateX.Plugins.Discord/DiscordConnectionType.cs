using AutomateX.Plugin.Sdk;

namespace AutomateX.Plugins.Discord;

[ConnectionType("discord", "Discord", Description = "A Discord channel webhook for posting messages.")]
public sealed class DiscordConnectionType : IConnectionType
{
    public IReadOnlyList<ConnectionField> Fields { get; } =
    [
        new("webhookUrl", "Webhook URL",
            HelpText: "Channel Settings → Integrations → Webhooks → New Webhook → Copy Webhook URL.",
            DocsUrl: "https://support.discord.com/hc/en-us/articles/228383668-Intro-to-Webhooks"),
    ];
}
