using System.Text.Json;
using AutomateX.Plugin.Sdk;

namespace AutomateX.Plugins.Telegram;

public sealed record TelegramSendConfig(
    string BotToken,
    string ChatId,
    [property: Multiline] string Text,
    string? ParseMode = null);

public sealed record TelegramSendResult(long MessageId);

[Action("telegram.send", "Telegram: Send Message",
    Description = "Sends a message via the Telegram Bot API (use {{connections.<name>.botToken}}). "
        + "chatId is the target chat/channel; optional parseMode is 'MarkdownV2' or 'HTML'.")]
public sealed class SendMessageAction : IAction<TelegramSendConfig, TelegramSendResult>
{
    private static readonly string[] AllowedParseModes = ["MarkdownV2", "Markdown", "HTML"];

    public async Task<TelegramSendResult> ExecuteAsync(
        TelegramSendConfig config,
        ActionContext context,
        CancellationToken cancellationToken = default)
    {
        Validate(config);

        var form = new Dictionary<string, string>
        {
            ["chat_id"] = config.ChatId,
            ["text"] = config.Text,
        };
        if (config.ParseMode is { Length: > 0 })
        {
            form["parse_mode"] = config.ParseMode;
        }

        using var response = await context.Http.PostAsync(
            $"https://api.telegram.org/bot{config.BotToken}/sendMessage",
            new FormUrlEncodedContent(form),
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"telegram.send failed: {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }

        using var json = JsonDocument.Parse(body);
        var messageId = json.RootElement.TryGetProperty("result", out var result)
            && result.TryGetProperty("message_id", out var id)
                ? id.GetInt64()
                : 0;
        return new TelegramSendResult(messageId);
    }

    private static void Validate(TelegramSendConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.BotToken))
        {
            throw new ArgumentException("telegram.send requires 'botToken'.");
        }

        if (string.IsNullOrWhiteSpace(config.ChatId))
        {
            throw new ArgumentException("telegram.send requires 'chatId'.");
        }

        if (string.IsNullOrWhiteSpace(config.Text))
        {
            throw new ArgumentException("telegram.send requires 'text'.");
        }

        if (config.ParseMode is { Length: > 0 } mode && !AllowedParseModes.Contains(mode))
        {
            throw new ArgumentException("telegram.send 'parseMode' must be 'MarkdownV2', 'Markdown', or 'HTML'.");
        }
    }
}
