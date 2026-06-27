using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutomateX.Modules.Executions;

internal sealed class ExecutionConfiguration : IEntityTypeConfiguration<Execution>
{
    public void Configure(EntityTypeBuilder<Execution> builder)
    {
        builder.Property(x => x.TriggeredBy).HasMaxLength(64);
        builder.Property(x => x.TriggerPayload).HasColumnType("jsonb");

        builder.HasOne<Workspaces.Workspace>()
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.WorkspaceId);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);

        // DB default 0 so the AddColumn migration backfills existing rows at top-level depth.
        builder.Property(x => x.Depth).HasDefaultValue(0);

        builder.HasMany(x => x.Steps)
            .WithOne()
            .HasForeignKey(x => x.ExecutionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.StartedAt);
        builder.HasIndex(x => x.ParentExecutionId);
    }
}

internal sealed class ForEachStateConfiguration : IEntityTypeConfiguration<ForEachState>
{
    public void Configure(EntityTypeBuilder<ForEachState> builder)
    {
        builder.Property(x => x.ItemsJson).HasColumnType("jsonb");
        builder.Property(x => x.ResultsJson).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.ExecutionId, x.StepOrder }).IsUnique();

        builder.HasOne<Execution>()
            .WithMany()
            .HasForeignKey(x => x.ExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
