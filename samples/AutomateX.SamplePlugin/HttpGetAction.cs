using AutomateX.Plugin.Sdk;

namespace AutomateX.SamplePlugin;

public sealed record HttpGetConfig(string Url);

public sealed record HttpGetResult(int StatusCode);

[Action("sample.httpget", "HTTP GET",
    Description = "GETs the configured URL and returns its status code — exercises the out-of-proc plugin HTTP egress guard.")]
public sealed class HttpGetAction : IAction<HttpGetConfig, HttpGetResult>
{
    public async Task<HttpGetResult> ExecuteAsync(
        HttpGetConfig config,
        ActionContext context,
        CancellationToken cancellationToken = default)
    {
        using var response = await context.Http.GetAsync(config.Url, cancellationToken);
        return new HttpGetResult((int)response.StatusCode);
    }
}
