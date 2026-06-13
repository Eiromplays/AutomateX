using AutomateX.Engine.Actions;

namespace AutomateX.Engine;

// A directed edge between two steps (by Order). A null Label is an unconditional ("always")
// link; labelled edges are the outcomes of a switch step.
public sealed record WorkflowEdgeDef(int From, int To, string? Label);

// Next = the step orders to run after the current one. Skipped = steps reachable only
// through a not-taken edge (recorded Skipped for the timeline, mirroring the gate).
public sealed record RoutingDecision(IReadOnlyList<int> Next, IReadOnlyList<int> Skipped);

// A join target's state once a predecessor finishes: Ready (run it), Skip (no live lane reaches
// it), or Wait (a predecessor is still running — a later lane will trigger it).
public enum StepReadiness
{
    Wait,
    Ready,
    Skip,
}

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

    // Whether a join target can run yet, given its incoming predecessors' states. The engine
    // pre-skips not-taken lanes (see Route), so a target's incoming sources are real predecessors:
    // run once all are terminal and at least one Succeeded; if all are terminal but none Succeeded
    // (every lane skipped/failed) the target is itself skipped; otherwise wait for a predecessor.
    public static StepReadiness Readiness(
        int target,
        IReadOnlyList<WorkflowEdgeDef> edges,
        Func<int, bool> isTerminal,
        Func<int, bool> isSucceeded,
        Func<int, bool> isFailed)
    {
        var incoming = edges.Where(e => e.To == target).Select(e => e.From).Distinct().ToList();
        if (incoming.Count == 0)
        {
            return StepReadiness.Wait; // entry steps aren't driven by joins
        }

        if (incoming.Any(source => !isTerminal(source)))
        {
            return StepReadiness.Wait;
        }

        // A failed dependency blocks this step (continue-on-failure): it's skipped, not run.
        if (incoming.Any(isFailed))
        {
            return StepReadiness.Skip;
        }

        return incoming.Any(isSucceeded) ? StepReadiness.Ready : StepReadiness.Skip;
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
