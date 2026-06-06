using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutomateX.Modules.Executions;

internal sealed class ExecutionConfiguration : IEntityTypeConfiguration<Execution>
{
    public void Configure(EntityTypeBuilder<Execution> builder)
    {
        builder.Property(x => x.TriggeredBy).HasMaxLength(64);
        builder.Property(x => x.TriggerPayload).HasColumnType("jsonb");
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);

        builder.HasMany(x => x.Steps)
            .WithOne()
            .HasForeignKey(x => x.ExecutionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.StartedAt);
    }
}
