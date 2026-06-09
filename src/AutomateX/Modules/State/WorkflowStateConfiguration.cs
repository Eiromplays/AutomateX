using AutomateX.Modules.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutomateX.Modules.State;

internal sealed class WorkflowStateConfiguration : IEntityTypeConfiguration<WorkflowState>
{
    public void Configure(EntityTypeBuilder<WorkflowState> builder)
    {
        // Composite PK doubles as the (WorkflowId, Key) prefix-scan index for ListByPrefix.
        builder.HasKey(x => new { x.WorkflowId, x.Key });

        // Capped so the key fits a btree index; hash very long ids (e.g. URLs) before use.
        builder.Property(x => x.Key).HasMaxLength(512);

        builder.HasOne<Workflow>()
            .WithMany()
            .HasForeignKey(x => x.WorkflowId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
