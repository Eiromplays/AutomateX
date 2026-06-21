using AutomateX.Plugin.Sdk;

namespace AutomateX.Plugins.Telegram;

[ConnectionType("telegram", "Telegram", Description = "A Telegram bot token for sending messages via the Bot API.")]
public sealed class TelegramConnectionType : IConnectionType, IConnectionTester
{
    public IReadOnlyList<ConnectionField> Fields { get; } =
    [
        new("botToken", "Bot token",
            HelpText: "Create a bot with @BotFather and copy the token (looks like 123456:ABC-DEF…).",
            DocsUrl: "https://core.telegram.org/bots#how-do-i-create-a-bot"),
    ];

    // getMe authenticates the token without sending anything — returns the bot's identity.
    public async Task<ConnectionTestResult> TestAsync(
        IReadOnlyDictionary<string, string> values, HttpClient http, CancellationToken cancellationToken)
    {
        if (!values.TryGetValue("botToken", out var token) || string.IsNullOrWhiteSpace(token))
        {
            return new ConnectionTestResult(false, "No bot token set.");
        }

        using var response = await http.GetAsync(
            $"https://api.telegram.org/bot{token}/getMe", cancellationToken);
        return response.IsSuccessStatusCode
            ? new ConnectionTestResult(true, "Bot token is valid.")
            : new ConnectionTestResult(false, $"Telegram rejected the token (HTTP {(int)response.StatusCode}).");
    }
}
