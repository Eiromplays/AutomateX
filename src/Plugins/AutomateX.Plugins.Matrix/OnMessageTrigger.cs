using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutomateX.Plugin.Sdk;
using Microsoft.Extensions.Logging;

namespace AutomateX.Plugins.Matrix;

public sealed record MatrixOnMessageConfig(
    string HomeserverUrl,
    string AccessToken,
    string? RoomId = null,
    int TimeoutMilliseconds = 30000);

[Trigger("matrix.onMessage", "Matrix: On Message",
    Description = "Fires when a message arrives in a joined room (sync long-polling). The bot's own "
        + "messages are always ignored — loop protection, so reply workflows can't trigger themselves. "
        + "History before the listener starts is skipped. Use {{connections.<name>.accessToken}}. "
        + "Payload: roomId, sender, body, msgType, eventId, timestamp.")]
public sealed class OnMessageTrigger : ITriggerListener<MatrixOnMessageConfig>
{
    public async Task RunAsync(MatrixOnMessageConfig config, TriggerContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.HomeserverUrl))
        {
            throw new ArgumentException("matrix.onMessage requires 'homeserverUrl'.");
        }

        if (string.IsNullOrWhiteSpace(config.AccessToken))
        {
            throw new ArgumentException("matrix.onMessage requires 'accessToken'.");
        }

        var baseUrl = config.HomeserverUrl.TrimEnd('/');
        var ownUserId = await GetAsync(context.Http, config.AccessToken,
            $"{baseUrl}/_matrix/client/v3/account/whoami", TimeSpan.FromSeconds(30), cancellationToken) is { } whoami
            && JsonNode.Parse(whoami)?["user_id"] is JsonValue userId
                ? userId.GetValue<string>()
                : throw new InvalidOperationException("matrix.onMessage could not resolve its own user id.");

        context.Logger.LogInformation("matrix.onMessage listening as {UserId}", ownUserId);

        string? since = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            // First sync (timeout=0) only establishes the token — history is skipped.
            var longPollMs = since is null ? 0 : config.TimeoutMilliseconds;
            var url = $"{baseUrl}/_matrix/client/v3/sync?timeout={longPollMs}"
                + (since is null ? "" : $"&since={Uri.EscapeDataString(since)}");

            // Per-request budget = the long-poll window + headroom; the shared client is
            // uncapped, so the listener owns the timeout for a hung connection.
            var body = await GetAsync(
                context.Http, config.AccessToken, url,
                TimeSpan.FromMilliseconds(longPollMs + 15000), cancellationToken);
            var root = JsonNode.Parse(body) ?? throw new InvalidOperationException("matrix.onMessage got an empty sync response.");

            var nextBatch = (root["next_batch"] as JsonValue)?.GetValue<string>()
                ?? throw new InvalidOperationException("matrix.onMessage sync response had no next_batch.");

            if (since is not null && root["rooms"]?["join"] is JsonObject rooms)
            {
                foreach (var (roomId, room) in rooms)
                {
                    if (config.RoomId is { Length: > 0 } && roomId != config.RoomId)
                    {
                        continue;
                    }

                    foreach (var rawEvent in room?["timeline"]?["events"] as JsonArray ?? [])
                    {
                        if ((rawEvent?["type"] as JsonValue)?.GetValue<string>() != "m.room.message")
                        {
                            continue;
                        }

                        var sender = (rawEvent?["sender"] as JsonValue)?.GetValue<string>();
                        if (sender is null || sender == ownUserId)
                        {
                            continue; // own messages never fire — loop protection
                        }

                        var payload = new JsonObject
                        {
                            ["roomId"] = roomId,
                            ["sender"] = sender,
                            ["body"] = rawEvent?["content"]?["body"]?.DeepClone(),
                            ["msgType"] = rawEvent?["content"]?["msgtype"]?.DeepClone(),
                            ["eventId"] = rawEvent?["event_id"]?.DeepClone(),
                            ["timestamp"] = rawEvent?["origin_server_ts"]?.DeepClone(),
                        };

                        await context.FireAsync(payload.ToJsonString());
                    }
                }
            }

            since = nextBatch;
        }
    }

    private static async Task<string> GetAsync(
        HttpClient http, string accessToken, string url, TimeSpan requestTimeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(requestTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await http.SendAsync(request, timeoutCts.Token);
        var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"matrix.onMessage request failed: {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(body)}");
        }

        return body;
    }

    private static string Truncate(string value) =>
        value.Length <= 500 ? value.Trim() : value[..500].Trim() + "…";
}
