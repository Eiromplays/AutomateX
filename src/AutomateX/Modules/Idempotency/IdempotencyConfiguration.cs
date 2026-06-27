using AutomateX.Modules.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutomateX.Modules.Idempotency;

internal sealed class IdempotencyConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        // Per-workflow scope: the composite PK is the dedup key and its lookup index.
        builder.HasKey(x => new { x.WorkflowId, x.Key });

        // Capped so the key fits a btree index; hash very long ids before use.
        builder.Property(x => x.Key).HasMaxLength(512);

        builder.HasOne<Workflow>()
            .WithMany()
            .HasForeignKey(x => x.WorkflowId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
