using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutomateX.Modules.Plugins;

internal sealed class PluginCatalogSourceConfiguration : IEntityTypeConfiguration<PluginCatalogSource>
{
    public void Configure(EntityTypeBuilder<PluginCatalogSource> builder)
    {
        builder.Property(x => x.Name).HasMaxLength(128);
        builder.Property(x => x.Url).HasMaxLength(2048);

        // One row per catalog URL — adding the same source twice is meaningless.
        builder.HasIndex(x => x.Url).IsUnique();
    }
}
