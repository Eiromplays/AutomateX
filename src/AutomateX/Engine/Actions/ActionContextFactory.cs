using AutomateX.Plugin.Sdk;

namespace AutomateX.Engine.Actions;

public sealed class ActionContextFactory(
    IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory, IServiceScopeFactory scopeFactory)
{
    public const string HttpClientName = "automatex-actions";

    // No total-timeout / retries — long-poll trigger listeners bound requests themselves.
    public const string TriggerHttpClientName = "automatex-triggers";

    public ActionContext Create(string actionType, ActionInvocation invocation) => new()
    {
        Logger = loggerFactory.CreateLogger($"AutomateX.Actions.{actionType}"),
        Http = httpClientFactory.CreateClient(HttpClientName),
        ExecutionId = invocation.ExecutionId,
        WorkflowId = invocation.WorkflowId,
        StepOrder = invocation.StepOrder,
        IdempotencyKey = invocation.IdempotencyKey,
    };

    public TriggerContext CreateTriggerContext(string triggerType, Triggers.TriggerRunnerContext runner) => new()
    {
        Logger = loggerFactory.CreateLogger($"AutomateX.Triggers.{triggerType}"),
        Http = httpClientFactory.CreateClient(TriggerHttpClientName),
        TriggerId = runner.TriggerId,
        WorkflowId = runner.WorkflowId,
        Fire = runner.Fire,
        State = new Triggers.WorkflowScopedTriggerState(scopeFactory, runner.WorkflowId, runner.TriggerId),
    };
}
