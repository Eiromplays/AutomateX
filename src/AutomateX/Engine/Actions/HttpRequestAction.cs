using System.Text;
using AutomateX.Plugin.Sdk;

namespace AutomateX.Engine.Actions;

public sealed record HttpRequestConfig(
    string Method,
    string Url,
    [property: Multiline] string? Body = null,
    Dictionary<string, string>? Headers = null,
    string? ContentType = null,
    int? TimeoutSeconds = null,
    bool FailOnErrorStatus = false,
    long? MaxResponseBytes = null);

public sealed record HttpRequestResult(int StatusCode, string Body, Dictionary<string, string> Headers);

[Action("http.request", "HTTP Request",
    Description = "Send an HTTP request and capture status, body and response headers. Bodies default to "
        + "application/json (override with contentType). Set failOnErrorStatus to fail the step — and trigger "
        + "retries — on non-2xx responses.")]
public sealed class HttpRequestAction : IAction<HttpRequestConfig, HttpRequestResult>
{
    public async Task<HttpRequestResult> ExecuteAsync(
        HttpRequestConfig config,
        ActionContext context,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Parse(config.Method), config.Url);
        if (config.Body is not null)
        {
            request.Content = new StringContent(config.Body, Encoding.UTF8, config.ContentType ?? "application/json");
        }

        foreach (var (name, value) in config.Headers ?? [])
        {
            // Request headers first; content-targeted headers (Content-Type, …) land on the body.
            if (!request.Headers.TryAddWithoutValidation(name, value))
            {
                request.Content?.Headers.TryAddWithoutValidation(name, value);
            }
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (config.TimeoutSeconds is { } timeout and > 0)
        {
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeout));
        }

        using var response = await context.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        var maxBytes = config.MaxResponseBytes is { } configured and > 0 ? configured : DefaultMaxResponseBytes;
        var body = await ReadCappedAsync(response.Content, maxBytes, timeoutCts.Token);

        if (config.FailOnErrorStatus && !response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"http.request got {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(body)}");
        }

        Dictionary<string, string> headers = [];
        foreach (var (name, values) in response.Headers.Concat(response.Content.Headers))
        {
            headers[name.ToLowerInvariant()] = string.Join(", ", values);
        }

        return new HttpRequestResult((int)response.StatusCode, body, headers);
    }

    // Cap the response we buffer so a hostile/huge body can't OOM the node. Default 5 MB; override
    // per step with maxResponseBytes. Rejects on Content-Length when present, and as a streaming
    // backstop for chunked responses with no length.
    private const long DefaultMaxResponseBytes = 5_000_000;

    private static async Task<string> ReadCappedAsync(HttpContent content, long maxBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is { } length && length > maxBytes)
        {
            throw new InvalidOperationException(
                $"http.request response is {length} bytes, over the {maxBytes}-byte cap (maxResponseBytes).");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new InvalidOperationException(
                    $"http.request response exceeds the {maxBytes}-byte cap (maxResponseBytes).");
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static string Truncate(string value) =>
        value.Length <= 1000 ? value.Trim() : value[..1000].Trim() + "…";
}
