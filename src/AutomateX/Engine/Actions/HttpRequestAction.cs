namespace AutomateX.Engine.Actions;

public sealed record HttpRequestConfig(string Method, string Url, string? Body = null);

public sealed record HttpRequestResult(int StatusCode, string Body);

public sealed class HttpRequestAction(IHttpClientFactory httpClientFactory)
    : IAction<HttpRequestConfig, HttpRequestResult>
{
    public const string ClientName = "automatex-actions";

    public async Task<HttpRequestResult> ExecuteAsync(
        HttpRequestConfig config,
        CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient(ClientName);

        using var request = new HttpRequestMessage(HttpMethod.Parse(config.Method), config.Url);
        if (config.Body is not null)
        {
            request.Content = new StringContent(config.Body);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        return new HttpRequestResult((int)response.StatusCode, body);
    }
}
