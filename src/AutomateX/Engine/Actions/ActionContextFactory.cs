using AutomateX.Plugin.Sdk;

namespace AutomateX.Engine.Actions;

public sealed class ActionContextFactory(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
{
    public const string HttpClientName = "automatex-actions";

    public ActionContext Create(string actionType) => new()
    {
        Logger = loggerFactory.CreateLogger($"AutomateX.Actions.{actionType}"),
        Http = httpClientFactory.CreateClient(HttpClientName),
    };
}
