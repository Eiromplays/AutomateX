using AutomateX.Modules.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutomateX.Modules.Variables;

internal sealed class WorkspaceEnvironmentConfiguration : IEntityTypeConfiguration<WorkspaceEnvironment>
{
    public void Configure(EntityTypeBuilder<WorkspaceEnvironment> builder)
    {
        builder.Property(x => x.Name).HasMaxLength(64);

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.WorkspaceId, x.Name }).IsUnique();
    }
}

internal sealed class VariableConfiguration : IEntityTypeConfiguration<Variable>
{
    public void Configure(EntityTypeBuilder<Variable> builder)
    {
        builder.Property(x => x.Name).HasMaxLength(128);

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Values)
            .WithOne()
            .HasForeignKey(x => x.VariableId)
            .OnDelete(DeleteBehavior.Cascade);

        // A name is unique within its scope: WorkflowId null (workspace) and a given WorkflowId are
        // distinct namespaces, so a workflow can shadow a workspace variable of the same name.
        builder.HasIndex(x => new { x.WorkspaceId, x.WorkflowId, x.Name }).IsUnique();
    }
}

internal sealed class VariableValueConfiguration : IEntityTypeConfiguration<VariableValue>
{
    public void Configure(EntityTypeBuilder<VariableValue> builder)
    {
        builder.HasOne<WorkspaceEnvironment>()
            .WithMany()
            .HasForeignKey(x => x.EnvironmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.VariableId, x.EnvironmentId }).IsUnique();
    }
}
