using AutomateX.Modules.Connections;
using AutomateX.Modules.Executions;
using AutomateX.Modules.Triggers;
using AutomateX.Modules.Workflows;
using AutomateX.Modules.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Database;

public sealed class AutomateXDbContext(DbContextOptions<AutomateXDbContext> options) : DbContext(options)
{
    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();

    public DbSet<Workflow> Workflows => Set<Workflow>();

    public DbSet<WorkflowVersion> WorkflowVersions => Set<WorkflowVersion>();

    public DbSet<WorkflowStep> WorkflowSteps => Set<WorkflowStep>();

    public DbSet<Trigger> Triggers => Set<Trigger>();

    public DbSet<Connection> Connections => Set<Connection>();

    public DbSet<Execution> Executions => Set<Execution>();

    public DbSet<StepExecution> StepExecutions => Set<StepExecution>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AutomateXDbContext).Assembly);
}
