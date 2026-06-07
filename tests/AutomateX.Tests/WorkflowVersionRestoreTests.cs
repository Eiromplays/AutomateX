using AutomateX.Modules.Workflows;
using Xunit;

namespace AutomateX.Tests;

// Rollback is git revert, not git reset: restoring vN appends a NEW version with vN's
// steps copied — history stays append-only and executions stay pinned.
public sealed class WorkflowVersionRestoreTests
{
    private static Workflow Build()
    {
        var workflow = Workflow.Create("wf", null);
        workflow.AddVersion([new StepDefinition("test.probe", "a", """{"marker":"v1"}""")]);
        workflow.AddVersion([
            new StepDefinition("test.probe", "b1", """{"marker":"v2-0"}"""),
            new StepDefinition("test.probe", "b2", """{"marker":"v2-1"}"""),
        ]);
        return workflow;
    }

    [Fact]
    public void Restore_copies_the_target_versions_steps_into_a_new_version()
    {
        var workflow = Build();

        var restored = workflow.RestoreVersion(1);

        Assert.Equal(3, restored.Version);
        var step = Assert.Single(restored.Steps);
        Assert.Equal("test.probe", step.ActionType);
        Assert.Equal("a", step.Name);
        Assert.Equal("""{"marker":"v1"}""", step.ConfigJson);
        Assert.Equal(0, step.Order);

        // A copy with its own identity — the original version is untouched.
        var original = workflow.Versions.First(x => x.Version == 1);
        Assert.NotEqual(original.Id, restored.Id);
        Assert.NotEqual(original.Steps[0].Id, restored.Steps[0].Id);
    }

    [Fact]
    public void Restore_preserves_step_order_and_count()
    {
        var workflow = Build();
        workflow.AddVersion([new StepDefinition("test.probe", "c", "{}")]); // v3

        var restored = workflow.RestoreVersion(2);

        Assert.Equal(4, restored.Version);
        Assert.Equal(2, restored.Steps.Count);
        Assert.Equal(["b1", "b2"], restored.Steps.OrderBy(x => x.Order).Select(x => x.Name));
    }

    [Fact]
    public void Restoring_the_latest_version_is_rejected()
    {
        var workflow = Build();

        var exception = Assert.Throws<InvalidOperationException>(() => workflow.RestoreVersion(2));

        Assert.Contains("latest", exception.Message);
        Assert.Equal(2, workflow.Versions.Count);
    }

    [Fact]
    public void Restoring_an_unknown_version_is_rejected()
    {
        var workflow = Build();

        Assert.Throws<InvalidOperationException>(() => workflow.RestoreVersion(99));
        Assert.Equal(2, workflow.Versions.Count);
    }
}
