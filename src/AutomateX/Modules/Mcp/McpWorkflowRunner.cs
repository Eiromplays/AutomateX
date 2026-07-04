using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Modules.Executions;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace AutomateX.Modules.Mcp;

// Settled = the run reached a terminal state within the timeout; otherwise the caller gets the
// ExecutionId to follow up on a long-running workflow.
public sealed record McpRunResult(bool Settled, ExecutionStatus? Status, string? Output, Guid ExecutionId);

// Runs a workflow and waits (bounded) for it to settle — the synchronous face an MCP tool call needs
// over the async engine. The wait is a plain status poll; nothing about the engine changes.
public sealed class McpWorkflowRunner(IMessageBus bus, AutomateXDbContext dbContext)
{
    public async Task<McpRunResult> RunAndWaitAsync(
        Guid workflowId, string triggeredBy, string? payload, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var executionId = Guid.CreateVersion7();
        await bus.PublishAsync(new RunWorkflow(executionId, workflowId, triggeredBy, payload));

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var status = await dbContext.Executions
                .AsNoTracking()
                .Where(x => x.Id == executionId)
                .Select(x => (ExecutionStatus?)x.Status)
                .FirstOrDefaultAsync(cancellationToken);

            if (status is { } settled && McpTools.IsTerminal(settled))
            {
                var steps = await dbContext.StepExecutions
                    .AsNoTracking()
                    .Where(x => x.ExecutionId == executionId)
                    .Select(x => new { x.StepOrder, x.Status, x.Output })
                    .ToListAsync(cancellationToken);

                var output = McpTools.TerminalOutput(steps.Select(x => (x.StepOrder, x.Status, x.Output)));
                return new McpRunResult(true, settled, output, executionId);
            }

            await Task.Delay(250, cancellationToken);
        }

        return new McpRunResult(false, null, null, executionId);
    }
}
