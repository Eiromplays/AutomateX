using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutomateX.Modules.Workflows;

internal sealed class WorkflowConfiguration : IEntityTypeConfiguration<Workflow>
{
    public void Configure(EntityTypeBuilder<Workflow> builder)
    {
        builder.Property(x => x.Name).HasMaxLength(128);

        builder.HasOne<Workspaces.Workspace>()
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.WorkspaceId);

        builder.HasMany(x => x.Versions)
            .WithOne()
            .HasForeignKey(x => x.WorkflowId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WorkflowVersionConfiguration : IEntityTypeConfiguration<WorkflowVersion>
{
    public void Configure(EntityTypeBuilder<WorkflowVersion> builder)
    {
        builder.HasIndex(x => new { x.WorkflowId, x.Version }).IsUnique();

        builder.HasMany(x => x.Steps)
            .WithOne()
            .HasForeignKey(x => x.WorkflowVersionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WorkflowStepConfiguration : IEntityTypeConfiguration<WorkflowStep>
{
    public void Configure(EntityTypeBuilder<WorkflowStep> builder)
    {
        builder.Property(x => x.Name).HasMaxLength(128);
        builder.Property(x => x.ActionType).HasMaxLength(128);
        builder.Property(x => x.ConfigJson).HasColumnType("jsonb");

        builder.HasIndex(x => new { x.WorkflowVersionId, x.Order }).IsUnique();
    }
}
