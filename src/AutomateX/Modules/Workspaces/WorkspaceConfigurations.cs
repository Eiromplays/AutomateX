using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutomateX.Modules.Workspaces;

internal sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        builder.Property(x => x.Name).HasMaxLength(128);
    }
}

internal sealed class WorkspaceMemberConfiguration : IEntityTypeConfiguration<WorkspaceMember>
{
    public void Configure(EntityTypeBuilder<WorkspaceMember> builder)
    {
        builder.Property(x => x.Email).HasMaxLength(256);
        builder.Property(x => x.Subject).HasMaxLength(256);
        builder.Property(x => x.Role).HasConversion<string>().HasMaxLength(16);

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.WorkspaceId, x.Email }).IsUnique();
    }
}
