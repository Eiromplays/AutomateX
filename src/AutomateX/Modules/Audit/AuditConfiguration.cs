using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutomateX.Modules.Audit;

internal sealed class AuditConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Actor).HasMaxLength(256);
        builder.Property(x => x.Action).HasMaxLength(64);
        builder.Property(x => x.TargetType).HasMaxLength(64);
        builder.Property(x => x.TargetId).HasMaxLength(128);
        builder.Property(x => x.Summary).HasMaxLength(1024);

        // The common read: a workspace's trail, newest first.
        builder.HasIndex(x => new { x.WorkspaceId, x.At });
    }
}
