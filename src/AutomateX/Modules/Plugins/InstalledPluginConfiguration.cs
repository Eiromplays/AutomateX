using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutomateX.Modules.Plugins;

internal sealed class InstalledPluginConfiguration : IEntityTypeConfiguration<InstalledPlugin>
{
    public void Configure(EntityTypeBuilder<InstalledPlugin> builder)
    {
        builder.Property(x => x.Name).HasMaxLength(256);
        builder.Property(x => x.PluginId).HasMaxLength(256);
        builder.Property(x => x.Version).HasMaxLength(64);
        builder.Property(x => x.Sha256).HasMaxLength(128);
        builder.Property(x => x.Publisher).HasMaxLength(256);
        builder.Property(x => x.InstalledBy).HasMaxLength(256);

        // One provenance row per installed plugin in a scope (global = Guid.Empty).
        builder.HasIndex(x => new { x.WorkspaceId, x.Name }).IsUnique();
    }
}
