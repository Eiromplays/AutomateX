using AutomateX.Engine;
using Xunit;

namespace AutomateX.Tests;

public sealed class WorkflowRouterTests
{
    private static WorkflowEdgeDef Edge(int from, int to, string? label = null) => new(from, to, label);

    [Fact]
    public void No_outgoing_edges_is_terminal()
    {
        var decision = WorkflowRouter.Route(5, [Edge(0, 5)], chosenLabel: null);

        Assert.Empty(decision.Next);
        Assert.Empty(decision.Skipped);
    }

    [Fact]
    public void Plain_step_follows_its_unlabeled_edge()
    {
        var decision = WorkflowRouter.Route(0, [Edge(0, 1)], chosenLabel: null);

        Assert.Equal([1], decision.Next);
        Assert.Empty(decision.Skipped);
    }

    [Fact]
    public void Switch_takes_the_matching_label_and_skips_the_other_lane()
    {
        // 0=switch → "ok":1, "default":2
        var edges = new[] { Edge(0, 1, "ok"), Edge(0, 2, "default") };

        var decision = WorkflowRouter.Route(0, edges, chosenLabel: "ok");

        Assert.Equal([1], decision.Next);
        Assert.Equal([2], decision.Skipped);
    }

    [Fact]
    public void Not_taken_lane_is_skipped_through_its_whole_subtree()
    {
        // 0=switch → "ok":1→3 ; "default":2→4
        var edges = new[]
        {
            Edge(0, 1, "ok"), Edge(1, 3),
            Edge(0, 2, "default"), Edge(2, 4),
        };

        var decision = WorkflowRouter.Route(0, edges, chosenLabel: "ok");

        Assert.Equal([1], decision.Next);
        Assert.Equal([2, 4], decision.Skipped.Order());
    }

    [Fact]
    public void Node_reachable_from_the_taken_path_too_is_not_skipped()
    {
        // Both lanes re-converge on 9 (a Phase-2 merge): taking "ok" must NOT skip 9,
        // even though 9 is also reachable via the not-taken "default" lane.
        var edges = new[]
        {
            Edge(0, 1, "ok"), Edge(1, 9),
            Edge(0, 2, "default"), Edge(2, 9),
        };

        var decision = WorkflowRouter.Route(0, edges, chosenLabel: "ok");

        Assert.Equal([1], decision.Next);
        Assert.Equal([2], decision.Skipped);
        Assert.DoesNotContain(9, decision.Skipped);
    }

    [Fact]
    public void Switch_falls_back_to_default_edge_when_label_has_no_edge()
    {
        var edges = new[] { Edge(0, 1, "ok"), Edge(0, 2, "default") };

        var decision = WorkflowRouter.Route(0, edges, chosenLabel: "nonexistent");

        Assert.Equal([2], decision.Next);
        Assert.Equal([1], decision.Skipped);
    }

    [Fact]
    public void Switch_with_no_match_and_no_default_terminates_and_skips_all()
    {
        var edges = new[] { Edge(0, 1, "a"), Edge(0, 2, "b") };

        var decision = WorkflowRouter.Route(0, edges, chosenLabel: "c");

        Assert.Empty(decision.Next);
        Assert.Equal([1, 2], decision.Skipped.Order());
    }
}
