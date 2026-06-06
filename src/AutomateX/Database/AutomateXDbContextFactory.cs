using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AutomateX.Database;

// Design-time only: lets `dotnet ef migrations add` build the model without the Aspire-injected connection string.
public sealed class AutomateXDbContextFactory : IDesignTimeDbContextFactory<AutomateXDbContext>
{
    public AutomateXDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AutomateXDbContext>()
            .UseNpgsql("Host=localhost;Database=automatex;Username=postgres;Password=postgres")
            .Options);
}
