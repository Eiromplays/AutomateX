using AutomateX.Engine.Plugins;
using AutomateX.Modules.Workspaces;
using FastEndpoints;

namespace AutomateX.Modules.Plugins.Features;

// Read-only plugin operations: process status (member-visible) and the log tail (instance-admin —
// logs can carry sensitive runtime data). Both target GLOBAL plugins by name.
public static class GetPluginStatus
{
    public sealed record Response(string State, int? Pid, DateTimeOffset? StartedAt, long MemoryBytes, int Restarts);

    public sealed class Endpoint(PluginAssemblies plugins, PluginProcessSupervisor supervisor, WorkspaceAccess access)
        : EndpointWithoutRequest<Response>
    {
        public override void Configure()
        {
            Get("plugins/{name}/status");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Viewer, ct) is null)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            if (ResolveDll(plugins, Route<string>("name")!) is not { } dll)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            var status = supervisor.Status(dll);
            await Send.OkAsync(
                new Response(status.State, status.Pid, status.StartedAt, status.MemoryBytes, status.Restarts), ct);
        }
    }

    internal static string? ResolveDll(PluginAssemblies plugins, string name) =>
        plugins.EnumeratePaths().FirstOrDefault(p => p.WorkspaceId is null && p.Name == name)?.DllPath;
}

public static class GetPluginLogs
{
    public sealed record LineResponse(long Seq, DateTimeOffset At, string Level, string? Source, string Message);

    public sealed record Response(List<LineResponse> Lines, long Cursor);

    public sealed class Endpoint(PluginAssemblies plugins, PluginProcessSupervisor supervisor, WorkspaceAccess access)
        : EndpointWithoutRequest<Response>
    {
        public override void Configure()
        {
            Get("plugins/{name}/logs");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            // Logs are instance-admin only — they can contain sensitive runtime data.
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Viewer, ct) is null || !access.IsInstanceAdmin(User))
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            if (GetPluginStatus.ResolveDll(plugins, Route<string>("name")!) is not { } dll)
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            var since = Query<long?>("since", isRequired: false) ?? 0;
            var lines = supervisor.LogsSince(dll, since)
                .Select(x => new LineResponse(x.Seq, x.At, x.Level, x.Source, x.Message))
                .ToList();

            await Send.OkAsync(new Response(lines, lines.Count > 0 ? lines[^1].Seq : since), ct);
        }
    }
}
