using AutomateX.Database;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Triggers.Features;

// A trigger's entry step must name a real step in the workflow's latest version. The engine still
// degrades a stale order to the first step, but the API is the gate so the builder can't author one.
internal static class TriggerEntry
{
    public static async Task ValidateOrThrowAsync(
        AutomateXDbContext dbContext, Guid workflowId, int entryStepOrder, Action<string> fail, CancellationToken ct)
    {
        var stepCount = await dbContext.WorkflowVersions
            .Where(v => v.WorkflowId == workflowId)
            .OrderByDescending(v => v.Version)
            .Select(v => v.Steps.Count)
            .FirstOrDefaultAsync(ct);

        if (entryStepOrder < 0 || entryStepOrder >= stepCount)
        {
            fail($"Entry step {entryStepOrder} is out of range — the workflow has {stepCount} step(s).");
        }
    }
}
