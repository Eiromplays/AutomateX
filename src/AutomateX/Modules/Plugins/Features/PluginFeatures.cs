using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Engine.Actions;
using AutomateX.Engine.Plugins;
using AutomateX.Modules.Workflows;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.Extensions.Options;

namespace AutomateX.Modules.Plugins.Features;

internal static class PluginFingerprints
{
    // MVID: changes on every distinct compilation — the build identity of a plugin.
    public static string Of(PluginAssembly plugin) =>
        plugin.Assembly.ManifestModule.ModuleVersionId.ToString("N")[..8];

    public static string? Find(PluginSnapshot snapshot, string scope, Guid workspaceId, string name)
    {
        IReadOnlyList<PluginAssembly> list = scope == "global"
            ? snapshot.Global
            : snapshot.Workspaces.TryGetValue(workspaceId, out var scoped) ? scoped : [];

        var plugin = list.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        return plugin is null ? null : Of(plugin);
    }
}

public static class GetPlugins
{
    public sealed class Endpoint(
        PluginAssemblies plugins,
        IOptions<EngineOptions> options,
        WorkspaceAccess access) : EndpointWithoutRequest<Response>
    {
        public override void Configure()
        {
            Get("plugins");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Viewer, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var snapshot = plugins.Current;
            var workspacePlugins = snapshot.Workspaces.TryGetValue(ws, out var scoped)
                ? scoped.Select(Describe).OrderBy(x => x.Name).ToList()
                : [];

            await Send.OkAsync(
                new Response(
                    options.Value.AllowPluginUpload,
                    snapshot.Global.Select(Describe).OrderBy(x => x.Name).ToList(),
                    workspacePlugins),
                ct);
        }

        // The MVID changes on every compilation — a build fingerprint that makes
        // "did the reload actually pick up my new code?" a fact instead of a guess.
        private static PluginInfo Describe(PluginAssembly plugin)
        {
            var fingerprint = PluginFingerprints.Of(plugin);
            DateTimeOffset? modifiedAt = null;
            try
            {
                if (plugin.Assembly.Location is { Length: > 0 } location && File.Exists(location))
                {
                    modifiedAt = File.GetLastWriteTimeUtc(location);
                }
            }
            catch (IOException)
            {
            }

            return new PluginInfo(
                plugin.Name,
                plugin.Assembly.GetName().Version?.ToString() ?? "0.0.0",
                fingerprint,
                modifiedAt);
        }
    }

    public sealed record PluginInfo(string Name, string Version, string Fingerprint, DateTimeOffset? ModifiedAt);

    public sealed record Response(bool UploadEnabled, List<PluginInfo> Global, List<PluginInfo> Workspace);
}

public static class ReloadPlugins
{
    // Instance-wide, like the action catalog itself; sits behind the auth gate.
    public sealed class Endpoint(PluginReloader reloader) : EndpointWithoutRequest<ReloadResult>
    {
        public override void Configure()
        {
            Post("actions/reload");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct) =>
            await Send.OkAsync(reloader.Reload(), ct);
    }
}

public static class UploadPlugin
{
    // scope: "global" (any authenticated caller) or "workspace" (Owner of the current
    // workspace). Both require the explicit Engine:AllowPluginUpload opt-in — an
    // uploaded plugin is code running in-process with full host trust.
    public sealed class Endpoint(
        PluginAssemblies plugins,
        PluginReloader reloader,
        IOptions<EngineOptions> options,
        WorkspaceAccess access) : EndpointWithoutRequest<Response>
    {
        public override void Configure()
        {
            Post("plugins/{scope}");
            AllowAnonymous();
            AllowFileUploads();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (!options.Value.AllowPluginUpload)
            {
                ThrowError("Plugin upload is disabled. Set Engine__AllowPluginUpload=true to enable it.");
            }

            var scope = Route<string>("scope");
            Guid workspaceId;
            string root;
            switch (scope)
            {
                case "global":
                    if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Viewer, ct) is not { } viewer)
                    {
                        await Send.ForbiddenAsync(ct);
                        return;
                    }

                    workspaceId = viewer;
                    root = plugins.GlobalRoot;
                    break;

                case "workspace":
                    if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Owner, ct) is not { } ws)
                    {
                        await Send.ForbiddenAsync(ct);
                        return;
                    }

                    workspaceId = ws;
                    root = plugins.WorkspaceRoot(ws);
                    break;

                default:
                    await Send.NotFoundAsync(ct);
                    return;
            }

            var file = Files.FirstOrDefault();
            if (file is null)
            {
                ThrowError("Attach the plugin as a zip file named <PluginName>.zip.");
                return;
            }

            var name = Path.GetFileNameWithoutExtension(file.FileName);
            var previousFingerprint = PluginFingerprints.Find(plugins.Current, scope, workspaceId, name);

            try
            {
                await using var stream = file.OpenReadStream();
                PluginArchive.Extract(stream, name, root);
            }
            catch (InvalidOperationException exception)
            {
                ThrowError(exception.Message);
            }

            var result = reloader.Reload();
            var fingerprint = PluginFingerprints.Find(plugins.Current, scope, workspaceId, name);
            await Send.OkAsync(
                new Response(name, scope, result.GlobalPlugins, result.WorkspacePlugins, previousFingerprint, fingerprint),
                ct);
        }
    }

    public sealed record Response(
        string Name,
        string Scope,
        int GlobalPlugins,
        int WorkspacePlugins,
        string? PreviousFingerprint,
        string? Fingerprint);
}

public static class DeletePlugin
{
    public sealed class Endpoint(
        PluginAssemblies plugins,
        PluginReloader reloader,
        ActionRegistry registry,
        AutomateXDbContext dbContext,
        IOptions<EngineOptions> options,
        WorkspaceAccess access) : EndpointWithoutRequest
    {
        public override void Configure()
        {
            Delete("plugins/{scope}/{name}");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (!options.Value.AllowPluginUpload)
            {
                ThrowError("Plugin management is disabled. Set Engine__AllowPluginUpload=true to enable it.");
            }

            var scope = Route<string>("scope");
            var name = Route<string>("name")!;

            try
            {
                PluginArchive.ValidateName(name);
            }
            catch (InvalidOperationException exception)
            {
                ThrowError(exception.Message);
            }

            Guid workspaceId;
            string root;
            switch (scope)
            {
                case "global":
                    if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Viewer, ct) is not { } viewer)
                    {
                        await Send.ForbiddenAsync(ct);
                        return;
                    }

                    workspaceId = viewer;
                    root = plugins.GlobalRoot;
                    break;

                case "workspace":
                    if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Owner, ct) is not { } ws)
                    {
                        await Send.ForbiddenAsync(ct);
                        return;
                    }

                    workspaceId = ws;
                    root = plugins.WorkspaceRoot(ws);
                    break;

                default:
                    await Send.NotFoundAsync(ct);
                    return;
            }

            var directory = Path.Combine(root, name);
            if (!Directory.Exists(directory))
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            // Guard: refuse while any workflow's latest version uses the plugin's
            // actions — unless ?force=true. Workspace deletes only scan their workspace.
            if (!Query<bool>("force", isRequired: false))
            {
                var global = scope == "global";
                var types = registry.ActionTypesFromSource(
                    global ? $"plugin:{name}" : $"workspace:{name}",
                    global ? null : workspaceId);
                var blocking = await PluginUsage.FindBlockingWorkflowsAsync(
                    dbContext, types, global ? null : workspaceId, ct);

                if (blocking.Count > 0)
                {
                    ThrowError($"'{name}' is used by the latest version of: {string.Join(", ", blocking)}. "
                        + "Those workflows will fail until the plugin returns. Pass force=true to delete anyway.");
                }
            }

            Directory.Delete(directory, recursive: true);
            reloader.Reload();
            await Send.NoContentAsync(ct);
        }
    }
}
