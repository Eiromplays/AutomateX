using System.Text;
using AutomateX.Modules.Triggers;
using AutomateX.Plugin.Sdk;
using Microsoft.Extensions.Options;

namespace AutomateX.Engine.Actions;

public sealed record WebhookSendConfig(
    string Url,
    [property: Multiline] string Body,
    Dictionary<string, string>? Headers = null,
    string? ContentType = null,
    string? SigningSecret = null,
    string? SignatureHeader = null);

public sealed record WebhookSendResult(int StatusCode, string Body);

[Action("webhook.send", "Webhook: Send",
    Description = "POST a payload to a URL. Optionally sign the body with an HMAC-SHA256 secret — the "
        + "signature (sha256=<hex>) goes in X-Webhook-Signature (override with signatureHeader), matching "
        + "AutomateX's own webhook verification. Fails the step on a non-2xx response.")]
public sealed class WebhookSendAction(IOptions<EngineOptions> options) : IAction<WebhookSendConfig, WebhookSendResult>
{
    public async Task<WebhookSendResult> ExecuteAsync(
        WebhookSendConfig config,
        ActionContext context,
        CancellationToken cancellationToken = default)
    {
        Validate(config);

        if (options.Value.BlockPrivateNetworkRequests)
        {
            await SsrfGuard.GuardAsync(config.Url, cancellationToken);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, config.Url)
        {
            Content = new StringContent(config.Body, Encoding.UTF8, config.ContentType ?? "application/json"),
        };

        // Sign the raw body so receivers (including other AutomateX webhooks) can verify integrity.
        if (config.SigningSecret is { Length: > 0 } secret)
        {
            var header = string.IsNullOrWhiteSpace(config.SignatureHeader)
                ? WebhookSecret.SignatureHeader
                : config.SignatureHeader;
            request.Headers.TryAddWithoutValidation(header, WebhookSecret.Sign(secret, config.Body));
        }

        foreach (var (name, value) in config.Headers ?? [])
        {
            if (!request.Headers.TryAddWithoutValidation(name, value))
            {
                request.Content?.Headers.TryAddWithoutValidation(name, value);
            }
        }

        // Forward the step's idempotency key so a compliant receiver dedups our retries — defense in
        // depth alongside the engine's own result cache. A user-set header of the same name wins.
        if (context.IdempotencyKey is { Length: > 0 } idempotencyKey
            && !(config.Headers?.Keys.Any(k => string.Equals(k, "Idempotency-Key", StringComparison.OrdinalIgnoreCase)) ?? false))
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        using var response = await context.Http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"webhook.send got {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(body)}");
        }

        return new WebhookSendResult((int)response.StatusCode, body);
    }

    private static void Validate(WebhookSendConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Url))
        {
            throw new ArgumentException("webhook.send requires 'url'.");
        }

        if (string.IsNullOrWhiteSpace(config.Body))
        {
            throw new ArgumentException("webhook.send requires 'body'.");
        }
    }

    private static string Truncate(string value) =>
        value.Length <= 1000 ? value.Trim() : value[..1000].Trim() + "…";
}
