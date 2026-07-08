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
        builder.HasIndex(x => new { x.ExecutionId, x.StepOrder }).IsUnique();

        builder.HasOne<Execution>()
            .WithMany()
            .HasForeignKey(x => x.ExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ForEachItemResultConfiguration : IEntityTypeConfiguration<ForEachItemResult>
{
    public void Configure(EntityTypeBuilder<ForEachItemResult> builder)
    {
        builder.Property(x => x.ResultJson).HasColumnType("jsonb");
        // The uniqueness that makes a redelivered child-resume a no-op (ON CONFLICT DO NOTHING) and lets
        // parallel completions write their own slot without contending.
        builder.HasIndex(x => new { x.ExecutionId, x.StepOrder, x.ItemIndex }).IsUnique();

        builder.HasOne<Execution>()
            .WithMany()
            .HasForeignKey(x => x.ExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
