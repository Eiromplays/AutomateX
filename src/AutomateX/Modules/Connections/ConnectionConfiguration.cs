using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutomateX.Modules.Connections;

internal sealed class ConnectionConfiguration : IEntityTypeConfiguration<Connection>
{
    public void Configure(EntityTypeBuilder<Connection> builder)
    {
        builder.Property(x => x.Name).HasMaxLength(64);
        builder.Property(x => x.Provider).HasMaxLength(64);

        builder.HasIndex(x => x.Name).IsUnique();
    }
}
