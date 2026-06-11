using AutomateX.Engine.Actions;

namespace AutomateX.Engine;

// A directed edge between two steps (by Order). A null Label is an unconditional ("always")
// link; labelled edges are the outcomes of a switch step.
public sealed record WorkflowEdgeDef(int From, int To, string? Label);

// Next = the step orders to run after the current one. Skipped = steps reachable only
// through a not-taken edge (recorded Skipped for the timeline, mirroring the gate).
public sealed record RoutingDecision(IReadOnlyList<int> Next, IReadOnlyList<int> Skipped);

// Pure graph routing — no DB, no engine state. Given the current step, the version's edges
// and (for a switch) the label its output chose, decide what runs next and what is skipped.
public static class WorkflowRouter
{
    public static RoutingDecision Route(int currentOrder, IReadOnlyList<WorkflowEdgeDef> edges, string? chosenLabel)
    {
        var outgoing = edges.Where(e => e.From == currentOrder).ToList();
        if (outgoing.Count == 0)
        {
            return new RoutingDecision([], []);
        }

        List<WorkflowEdgeDef> taken;
        if (chosenLabel is not null)
        {
            // Switch: the edge matching the chosen label, else the "default" edge.
            taken = outgoing.Where(e => e.Label == chosenLabel).ToList();
            if (taken.Count == 0)
            {
                taken = outgoing.Where(e => e.Label == Switch.DefaultLabel).ToList();
            }
        }
        else
        {
            // Plain step: unconditional edges only.
            taken = outgoing.Where(e => e.Label is null).ToList();
        }

        var next = taken.Select(e => e.To).Distinct().ToList();
        var reachableFromTaken = Reachable(next, edges);

        var notTakenTargets = outgoing.Select(e => e.To).Where(to => !next.Contains(to));
        var skipped = Reachable(notTakenTargets, edges)
            .Where(order => !reachableFromTaken.Contains(order))
            .ToList();

        return new RoutingDecision(next, skipped);
    }

    private static HashSet<int> Reachable(IEnumerable<int> starts, IReadOnlyList<WorkflowEdgeDef> edges)
    {
        HashSet<int> seen = [];
        Queue<int> queue = new(starts);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!seen.Add(node))
            {
                continue;
            }

            foreach (var edge in edges.Where(e => e.From == node))
            {
                queue.Enqueue(edge.To);
            }
        }

        return seen;
    }
}
