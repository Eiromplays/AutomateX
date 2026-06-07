using AutomateX.Plugin.Sdk;

namespace AutomateX.Engine.Actions;

public sealed class ActionContextFactory(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
{
    public const string HttpClientName = "automatex-actions";

    public ActionContext Create(string actionType, ActionInvocation invocation) => new()
    {
        Logger = loggerFactory.CreateLogger($"AutomateX.Actions.{actionType}"),
        Http = httpClientFactory.CreateClient(HttpClientName),
        ExecutionId = invocation.ExecutionId,
        WorkflowId = invocation.WorkflowId,
        StepOrder = invocation.StepOrder,
    };

    public TriggerContext CreateTriggerContext(string triggerType, Triggers.TriggerRunnerContext runner) => new()
    {
        Logger = loggerFactory.CreateLogger($"AutomateX.Triggers.{triggerType}"),
        Http = httpClientFactory.CreateClient(HttpClientName),
        TriggerId = runner.TriggerId,
        WorkflowId = runner.WorkflowId,
        Fire = runner.Fire,
    };
}
