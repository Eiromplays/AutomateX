using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutomateX.Modules.Executions;

internal sealed class StepExecutionConfiguration : IEntityTypeConfiguration<StepExecution>
{
    public void Configure(EntityTypeBuilder<StepExecution> builder)
    {
        builder.Property(x => x.ActionType).HasMaxLength(128);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
    }
}
