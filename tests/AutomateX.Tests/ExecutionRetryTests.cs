using AutomateX.Engine;
using AutomateX.Modules.Executions;
using Xunit;

namespace AutomateX.Tests;

// Retry = a fresh RunWorkflow carrying the ORIGINAL trigger payload untouched
// (templates must see exactly what the first run saw), on the latest version
// (RunWorkflow resolves latest by design), with provenance in triggeredBy only.
public sealed class ExecutionRetryTests
{
    [Fact]
    public void Replay_preserves_payload_and_marks_provenance()
    {
        var original = Execution.Start(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            "webhook:abc", """{"x":"original"}""");
        original.Fail();

        var message = ExecutionRetry.Replay(original);

        Assert.Equal(original.WorkflowId, message.WorkflowId);
        Assert.Equal("""{"x":"original"}""", message.Payload);
        Assert.Equal($"retry:{original.Id}", message.TriggeredBy);
        Assert.NotEqual(original.Id, message.ExecutionId);
    }

    [Fact]
    public void Replay_of_a_payloadless_execution_stays_payloadless()
    {
        var original = Execution.Start(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "manual");
        original.Complete();

        var message = ExecutionRetry.Replay(original);

        Assert.Null(message.Payload);
    }

    [Theory]
    [InlineData(ExecutionStatus.Pending, false)]
    [InlineData(ExecutionStatus.Running, false)]
    [InlineData(ExecutionStatus.Succeeded, true)]
    [InlineData(ExecutionStatus.Failed, true)]
    public void Only_terminal_executions_can_be_retried(ExecutionStatus status, bool expected) =>
        Assert.Equal(expected, ExecutionRetry.CanRetry(status));

    [Fact]
    public void Provenance_round_trips_for_lineage_displays()
    {
        var original = Execution.Start(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "manual");
        original.Fail();

        var message = ExecutionRetry.Replay(original);

        Assert.Equal(original.Id, ExecutionRetry.GetOriginalExecutionId(message.TriggeredBy));
        Assert.Null(ExecutionRetry.GetOriginalExecutionId("webhook:abc"));
        Assert.Null(ExecutionRetry.GetOriginalExecutionId("retry:not-a-guid"));
    }
}
