using AutomateX.Database;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Workflows;

public static class WorkflowDeletion
{
    // Executions are deliberately not FK-bound to workflows (history outlives nothing here by
    // design choice: deleting a workflow deletes its history) — so the cleanup is explicit,
    // atomic with the workflow row. Step executions cascade from executions at the DB level.
    public static async Task<bool> DeleteAsync(
        AutomateXDbContext dbContext, Guid workflowId, Guid workspaceId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await dbContext.Executions
            .Where(x => x.WorkflowId == workflowId && x.WorkspaceId == workspaceId)
            .ExecuteDeleteAsync(cancellationToken);

        var deleted = await dbContext.Workflows
            .Where(x => x.Id == workflowId && x.WorkspaceId == workspaceId)
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return deleted > 0;
    }
}
