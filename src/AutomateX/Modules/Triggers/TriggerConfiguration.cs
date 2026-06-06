using AutomateX.Modules.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutomateX.Modules.Triggers;

internal sealed class TriggerConfiguration : IEntityTypeConfiguration<Trigger>
{
    public void Configure(EntityTypeBuilder<Trigger> builder)
    {
        builder.Property(x => x.Type).HasMaxLength(32);
        builder.Property(x => x.ConfigJson).HasColumnType("jsonb");

        builder.HasOne<Workflow>()
            .WithMany()
            .HasForeignKey(x => x.WorkflowId)
            .OnDelete(DeleteBehavior.Cascade);

        // Covers the scheduler's due-trigger claim query.
        builder.HasIndex(x => new { x.Type, x.Enabled, x.NextRunAt });
    }
}
