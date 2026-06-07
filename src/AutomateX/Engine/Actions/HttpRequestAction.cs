using System.Text;
using AutomateX.Plugin.Sdk;

namespace AutomateX.Engine.Actions;

public sealed record HttpRequestConfig(
    string Method,
    string Url,
    string? Body = null,
    Dictionary<string, string>? Headers = null,
    string? ContentType = null,
    int? TimeoutSeconds = null,
    bool FailOnErrorStatus = false);

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

        using var response = await context.Http.SendAsync(request, timeoutCts.Token);
        var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

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

    private static string Truncate(string value) =>
        value.Length <= 1000 ? value.Trim() : value[..1000].Trim() + "…";
}
