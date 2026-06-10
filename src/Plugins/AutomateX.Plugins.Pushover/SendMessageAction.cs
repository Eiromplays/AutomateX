using System.Globalization;
using System.Text.Json;
using AutomateX.Plugin.Sdk;

namespace AutomateX.Plugins.Pushover;

public sealed record PushoverSendConfig(
    string AppToken,
    string UserKey,
    string Message,
    string? Title = null,
    int? Priority = null);

public sealed record PushoverSendResult(int Status, string Request);

[Action("pushover.send", "Pushover: Send Notification",
    Description = "Sends a mobile push via Pushover (use {{connections.<name>.appToken}} and userKey). "
        + "Optional title and priority (-2 lowest … 2 emergency).")]
public sealed class SendMessageAction : IAction<PushoverSendConfig, PushoverSendResult>
{
    private const string MessagesEndpoint = "https://api.pushover.net/1/messages.json";

    public async Task<PushoverSendResult> ExecuteAsync(
        PushoverSendConfig config,
        ActionContext context,
        CancellationToken cancellationToken = default)
    {
        Validate(config);

        var form = new Dictionary<string, string>
        {
            ["token"] = config.AppToken,
            ["user"] = config.UserKey,
            ["message"] = config.Message,
        };
        if (config.Title is { Length: > 0 })
        {
            form["title"] = config.Title;
        }

        if (config.Priority is { } priority)
        {
            form["priority"] = priority.ToString(CultureInfo.InvariantCulture);
        }

        using var response = await context.Http.PostAsync(
            MessagesEndpoint, new FormUrlEncodedContent(form), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"pushover.send failed: {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var status = root.TryGetProperty("status", out var s) ? s.GetInt32() : 0;
        var request = root.TryGetProperty("request", out var r) ? r.GetString() ?? "" : "";
        return new PushoverSendResult(status, request);
    }

    private static void Validate(PushoverSendConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.AppToken))
        {
            throw new ArgumentException("pushover.send requires 'appToken'.");
        }

        if (string.IsNullOrWhiteSpace(config.UserKey))
        {
            throw new ArgumentException("pushover.send requires 'userKey'.");
        }

        if (string.IsNullOrWhiteSpace(config.Message))
        {
            throw new ArgumentException("pushover.send requires 'message'.");
        }

        if (config.Priority is { } priority and (< -2 or > 2))
        {
            throw new ArgumentException("pushover.send 'priority' must be between -2 and 2.");
        }
    }
}
