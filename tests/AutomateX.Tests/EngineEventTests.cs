using AutomateX.Modules.Executions;
using AutomateX.Plugin.Sdk;
using Xunit;

namespace AutomateX.Tests;

public sealed class EngineEventTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task Successful_execution_emits_lifecycle_events_in_order()
    {
        fixture.ProbeAction.Reset();
        fixture.EventListener.Reset();

        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        var executionId = await TestData.ExecuteAsync(fixture.Host, workflowId);
        await WaitForEventAsync<ExecutionCompleted>(executionId);

        var events = EventsFor(executionId);
        Assert.Equal(
            [typeof(ExecutionStarted), typeof(StepCompleted), typeof(ExecutionCompleted)],
            events.Select(x => x.GetType()));
    }

    [Fact]
    public async Task Failing_listener_never_breaks_the_execution()
    {
        fixture.ProbeAction.Reset();
        fixture.EventListener.Reset(throwOnExecutionStarted: true);

        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        var executionId = await TestData.ExecuteAsync(fixture.Host, workflowId);

        var execution = await TestData.WaitForTerminalAsync(fixture.Host, executionId, EventTimeout);
        await WaitForEventAsync<ExecutionCompleted>(executionId);

        // The throwing listener neither broke the execution nor lost its later events.
        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
        Assert.Contains(EventsFor(executionId), x => x is StepCompleted);
    }

    [Fact]
    public async Task Step_failures_emit_StepFailed_with_attempts_and_retry_flag()
    {
        fixture.ProbeAction.Reset(failuresBeforeSuccess: 2);
        fixture.EventListener.Reset();

        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        var executionId = await TestData.ExecuteAsync(fixture.Host, workflowId);
        await WaitForEventAsync<ExecutionCompleted>(executionId);

        var failures = EventsFor(executionId).OfType<StepFailed>().ToList();
        Assert.Equal(2, failures.Count);
        Assert.All(failures, x => Assert.True(x.WillRetry));
        Assert.Equal([1, 2], failures.Select(x => x.Attempts));
    }

    [Fact]
    public async Task Exhausted_retries_emit_final_StepFailed_and_ExecutionFailed()
    {
        fixture.ProbeAction.Reset(failuresBeforeSuccess: int.MaxValue);
        fixture.EventListener.Reset();

        var workflowId = await TestData.SeedWorkflowAsync(fixture.Host, stepCount: 1);
        var executionId = await TestData.ExecuteAsync(fixture.Host, workflowId);
        await WaitForEventAsync<ExecutionFailed>(executionId);

        var events = EventsFor(executionId);
        var lastFailure = events.OfType<StepFailed>().Last();
        Assert.False(lastFailure.WillRetry);
        Assert.Equal(3, lastFailure.Attempts);
        Assert.DoesNotContain(events, x => x is ExecutionCompleted);
    }

    private List<IEngineEvent> EventsFor(Guid executionId) =>
        fixture.EventListener.Events.Where(x => ExecutionIdOf(x) == executionId).ToList();

    private static Guid ExecutionIdOf(IEngineEvent engineEvent) => engineEvent switch
    {
        ExecutionStarted x => x.ExecutionId,
        StepCompleted x => x.ExecutionId,
        StepFailed x => x.ExecutionId,
        ExecutionCompleted x => x.ExecutionId,
        ExecutionFailed x => x.ExecutionId,
        _ => Guid.Empty,
    };

    private async Task WaitForEventAsync<TEvent>(Guid executionId)
        where TEvent : IEngineEvent
    {
        var deadline = DateTimeOffset.UtcNow + EventTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (fixture.EventListener.Events.OfType<TEvent>().Any(x => ExecutionIdOf(x) == executionId))
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException(
            $"Event {typeof(TEvent).Name} for execution {executionId} was not observed within {EventTimeout}.");
    }
}
