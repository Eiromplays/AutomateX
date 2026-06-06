using AutomateX.Plugin.Sdk;

namespace AutomateX.Engine.Actions;

public sealed record HttpRequestConfig(string Method, string Url, string? Body = null);

public sealed record HttpRequestResult(int StatusCode, string Body);

[Action("http.request", "HTTP Request", Description = "Send an HTTP request and capture the response.")]
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
            request.Content = new StringContent(config.Body);
        }

        using var response = await context.Http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        return new HttpRequestResult((int)response.StatusCode, body);
    }
}
