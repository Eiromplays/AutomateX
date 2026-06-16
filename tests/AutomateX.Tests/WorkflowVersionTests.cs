using AutomateX.Modules.Workflows;
using Xunit;

namespace AutomateX.Tests;

// History pruning rules: a past version can be removed, but the latest (live) version can't,
// and a non-existent version is an error. Execution-reference protection lives in the endpoint.
public sealed class WorkflowVersionTests
{
    private static Workflow WithVersions(int count)
    {
        var workflow = Workflow.Create("wf", null);
        for (var i = 0; i < count; i++)
        {
            workflow.AddVersion([new StepDefinition("gate", null, "{}")]);
        }

        return workflow;
    }

    [Fact]
    public void RemoveVersion_removes_a_past_version()
    {
        var workflow = WithVersions(3); // v1, v2, v3

        workflow.RemoveVersion(1);

        Assert.Equal([2, 3], workflow.Versions.Select(v => v.Version).OrderBy(x => x));
    }

    [Fact]
    public void RemoveVersion_rejects_the_latest()
    {
        var workflow = WithVersions(2); // v2 is latest

        var exception = Assert.Throws<InvalidOperationException>(() => workflow.RemoveVersion(2));
        Assert.Contains("latest", exception.Message);
    }

    [Fact]
    public void RemoveVersion_rejects_a_missing_version()
    {
        var workflow = WithVersions(2);

        Assert.Throws<InvalidOperationException>(() => workflow.RemoveVersion(99));
    }
}
