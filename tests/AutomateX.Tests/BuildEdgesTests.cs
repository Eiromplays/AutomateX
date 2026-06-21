using AutomateX.Modules.Workflows.Features;
using Xunit;

namespace AutomateX.Tests;

// Edge validation: out-of-range targets are rejected, and "error" is reserved as the single
// failure-path edge per step.
public sealed class BuildEdgesTests
{
    private static CreateWorkflow.EdgeRequest Edge(int from, int to, string? label = null) => new(from, to, label);

    private static List<string> Collect(int stepCount, params CreateWorkflow.EdgeRequest[] edges)
    {
        var errors = new List<string>();
        CreateWorkflow.BuildEdges(edges, stepCount, errors.Add);
        return errors;
    }

    [Fact]
    public void Out_of_range_edge_is_rejected()
    {
        var errors = Collect(2, Edge(0, 5));
        Assert.Contains(errors, e => e.Contains("doesn't exist"));
    }

    [Fact]
    public void One_error_edge_per_step_is_allowed()
    {
        var errors = Collect(3, Edge(0, 1, "error"), Edge(0, 2, null));
        Assert.Empty(errors);
    }

    [Fact]
    public void Two_error_edges_from_one_step_are_rejected()
    {
        var errors = Collect(3, Edge(0, 1, "error"), Edge(0, 2, "error"));
        Assert.Contains(errors, e => e.Contains("more than one error edge"));
    }

    [Fact]
    public void Error_edges_from_different_steps_are_fine()
    {
        var errors = Collect(4, Edge(0, 2, "error"), Edge(1, 3, "error"));
        Assert.Empty(errors);
    }
}
