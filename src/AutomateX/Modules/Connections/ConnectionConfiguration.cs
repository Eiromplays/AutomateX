using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutomateX.Modules.Connections;

internal sealed class ConnectionConfiguration : IEntityTypeConfiguration<Connection>
{
    public void Configure(EntityTypeBuilder<Connection> builder)
    {
        builder.Property(x => x.Name).HasMaxLength(64);
        builder.Property(x => x.Provider).HasMaxLength(64);

        builder.HasOne<Workspaces.Workspace>()
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Names are the template handle — unique per workspace, not globally.
        builder.HasIndex(x => new { x.WorkspaceId, x.Name }).IsUnique();
    }
}
