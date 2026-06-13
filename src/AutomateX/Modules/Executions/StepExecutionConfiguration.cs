using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutomateX.Modules.Executions;

internal sealed class StepExecutionConfiguration : IEntityTypeConfiguration<StepExecution>
{
    public void Configure(EntityTypeBuilder<StepExecution> builder)
    {
        builder.Property(x => x.ActionType).HasMaxLength(128);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);

        // One row per (execution, step) — the uniqueness that makes the parallel-dispatch
        // "claim" atomic (INSERT … ON CONFLICT DO NOTHING) so a join step runs exactly once.
        builder.HasIndex(x => new { x.ExecutionId, x.StepOrder }).IsUnique();
    }
}
