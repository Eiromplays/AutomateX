using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AutomateX.Plugin.Sdk;

namespace AutomateX.Plugins.Matrix;

public sealed record MatrixSendConfig(
    string HomeserverUrl,
    string AccessToken,
    string RoomId,
    string Message,
    string? Html = null,
    string MsgType = "m.text");

public sealed record MatrixSendResult(string EventId, string RoomId);

[Action("matrix.send", "Matrix: Send Message",
    Description = "Sends a message to a Matrix room (use {{connections.<name>.accessToken}} for the token). "
        + "Transaction ids are deterministic per execution step, so engine retries are deduplicated by the "
        + "homeserver — a notification can never double-send."
        + "updated - this is a test")]
public sealed class SendMessageAction : IAction<MatrixSendConfig, MatrixSendResult>
{
    public async Task<MatrixSendResult> ExecuteAsync(
        MatrixSendConfig config,
        ActionContext context,
        CancellationToken cancellationToken = default)
    {
        Validate(config);

        var txnId = $"automatex-{context.ExecutionId:N}-{context.StepOrder}";
        var url = $"{config.HomeserverUrl.TrimEnd('/')}/_matrix/client/v3/rooms/"
            + $"{Uri.EscapeDataString(config.RoomId)}/send/m.room.message/{txnId}";

        var content = new Dictionary<string, string>
        {
            ["msgtype"] = config.MsgType,
            ["body"] = config.Message,
        };
        if (config.Html is { Length: > 0 })
        {
            content["format"] = "org.matrix.custom.html";
            content["formatted_body"] = config.Html;
        }

        // Token rides the request, never the shared HttpClient.
        using var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(content) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken);

        using var response = await context.Http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"matrix.send failed: {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }

        using var json = JsonDocument.Parse(body);
        var eventId = json.RootElement.TryGetProperty("event_id", out var id) ? id.GetString() ?? "" : "";
        return new MatrixSendResult(eventId, config.RoomId);
    }

    private static void Validate(MatrixSendConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.HomeserverUrl))
        {
            throw new ArgumentException("matrix.send requires 'homeserverUrl'.");
        }

        if (string.IsNullOrWhiteSpace(config.AccessToken))
        {
            throw new ArgumentException("matrix.send requires 'accessToken'.");
        }

        if (string.IsNullOrWhiteSpace(config.RoomId))
        {
            throw new ArgumentException("matrix.send requires 'roomId'.");
        }

        if (string.IsNullOrWhiteSpace(config.Message))
        {
            throw new ArgumentException("matrix.send requires 'message'.");
        }

        if (config.MsgType is not ("m.text" or "m.notice"))
        {
            throw new ArgumentException("matrix.send supports msgType 'm.text' or 'm.notice'.");
        }
    }
}
