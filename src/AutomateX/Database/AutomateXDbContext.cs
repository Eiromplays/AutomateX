using AutomateX.Modules.Connections;
using AutomateX.Modules.Executions;
using AutomateX.Modules.State;
using AutomateX.Modules.Triggers;
using AutomateX.Modules.Workflows;
using AutomateX.Modules.Workspaces;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Database;

// IDataProtectionKeyContext: the key ring that encrypts the auth cookie lives in
// Postgres, so it survives app restarts/container recreation — without it, every
// restart regenerates the keys and invalidates all sessions (forced re-login).
public sealed class AutomateXDbContext(DbContextOptions<AutomateXDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();

    public DbSet<Workflow> Workflows => Set<Workflow>();

    public DbSet<WorkflowVersion> WorkflowVersions => Set<WorkflowVersion>();

    public DbSet<WorkflowStep> WorkflowSteps => Set<WorkflowStep>();

    public DbSet<WorkflowEdge> WorkflowEdges => Set<WorkflowEdge>();

    public DbSet<Trigger> Triggers => Set<Trigger>();

    public DbSet<Connection> Connections => Set<Connection>();

    public DbSet<Execution> Executions => Set<Execution>();

    public DbSet<StepExecution> StepExecutions => Set<StepExecution>();

    public DbSet<WorkflowState> WorkflowStates => Set<WorkflowState>();

    public DbSet<ForEachState> ForEachStates => Set<ForEachState>();

    public DbSet<Modules.Idempotency.IdempotencyRecord> IdempotencyRecords => Set<Modules.Idempotency.IdempotencyRecord>();

    public DbSet<Modules.Audit.AuditEntry> AuditEntries => Set<Modules.Audit.AuditEntry>();

    public DbSet<Modules.Workspaces.WorkspaceKey> WorkspaceKeys => Set<Modules.Workspaces.WorkspaceKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AutomateXDbContext).Assembly);
}
