using AutomateX.Database;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Plugins.Features;

// Opt a globally-installed, catalog-sourced plugin into (or out of) automatic updates. Instance-admin;
// requires provenance — a plugin dropped on disk or uploaded as a zip has no source to update from.
public static class SetPluginAutoUpdate
{
    public sealed record Request(string? Name, bool Enabled);

    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access, Audit.IAuditSink audit)
        : Endpoint<Request>
    {
        public override void Configure()
        {
            Put("plugins/catalog/auto-update");
            AllowAnonymous();
        }

        public override async Task HandleAsync(Request req, CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Viewer, ct) is null || !access.IsInstanceAdmin(HttpContext))
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(req.Name))
            {
                ThrowError("A plugin name is required.");
            }

            var installed = await dbContext.InstalledPlugins
                .FirstOrDefaultAsync(x => x.WorkspaceId == Guid.Empty && x.Name == req.Name, ct);
            if (installed is null)
            {
                ThrowError($"'{req.Name}' has no catalog provenance — only catalog-installed plugins can auto-update.");
                return;
            }

            installed.SetAutoUpdate(req.Enabled);
            await dbContext.SaveChangesAsync(ct);

            await audit.RecordAsync(
                "plugin.autoupdate", null, WorkspaceAccess.GetActor(HttpContext),
                "plugin", req.Name!, req.Enabled ? "enabled" : "disabled", ct);
            await Send.NoContentAsync(ct);
        }
    }
}
