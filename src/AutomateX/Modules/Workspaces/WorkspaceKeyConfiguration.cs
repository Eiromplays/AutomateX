using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutomateX.Modules.Workspaces;

internal sealed class WorkspaceKeyConfiguration : IEntityTypeConfiguration<WorkspaceKey>
{
    public void Configure(EntityTypeBuilder<WorkspaceKey> builder)
    {
        builder.HasKey(x => new { x.WorkspaceId, x.Version });

        builder.Property(x => x.WrappedDek).HasMaxLength(256);

        // Fast lookup of the active DEK for a workspace.
        builder.HasIndex(x => new { x.WorkspaceId, x.Active });

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
