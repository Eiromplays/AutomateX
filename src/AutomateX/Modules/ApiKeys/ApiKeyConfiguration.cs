using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutomateX.Modules.ApiKeys;

internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.Property(x => x.Name).HasMaxLength(128);
        builder.Property(x => x.Prefix).HasMaxLength(32);
        builder.Property(x => x.CreatedBy).HasMaxLength(256);

        // Prefix is the auth lookup key — unique, and every request hits this index.
        builder.HasIndex(x => x.Prefix).IsUnique();
        builder.HasIndex(x => x.WorkspaceId);
    }
}
