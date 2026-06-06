namespace AutomateX.Engine.Actions;

public sealed class HttpRequestExecutor(HttpRequestAction action)
    : ActionExecutor<HttpRequestConfig, HttpRequestResult>("http.request")
{
    protected override Task<HttpRequestResult> ExecuteAsync(HttpRequestConfig config, CancellationToken cancellationToken)
        => action.ExecuteAsync(config, cancellationToken);
}
