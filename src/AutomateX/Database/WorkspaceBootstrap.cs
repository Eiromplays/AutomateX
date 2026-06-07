using AutomateX.Modules.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Database;

public static class WorkspaceBootstrap
{
    // The Default workspace must always exist — pre-workspace rows reference its
    // well-known id, and header-less requests resolve to it.
    public static async Task EnsureDefaultAsync(AutomateXDbContext dbContext, CancellationToken cancellationToken = default)
    {
        if (!await dbContext.Workspaces.AnyAsync(x => x.Id == Workspace.DefaultId, cancellationToken))
        {
            dbContext.Workspaces.Add(Workspace.CreateDefault());
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
