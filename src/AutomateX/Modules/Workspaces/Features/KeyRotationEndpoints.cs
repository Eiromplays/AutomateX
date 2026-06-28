using AutomateX.Engine.Security;
using AutomateX.Modules.Audit;
using FastEndpoints;

namespace AutomateX.Modules.Workspaces.Features;

// Rotate one workspace's data-encryption key, re-encrypting its connections. Instance-admin only.
public static class RotateWorkspaceKey
{
    public sealed class Endpoint(KeyRotationService rotation, WorkspaceAccess access, IAuditSink audit)
        : EndpointWithoutRequest<Response>
    {
        public override void Configure()
        {
            Post("workspaces/{id}/rotate-key");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (!access.IsInstanceAdmin(User))
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var id = Route<Guid>("id");
            var (version, reEncrypted) = await rotation.RotateWorkspaceAsync(id, ct);
            await audit.RecordAsync(
                "key.rotate-workspace", id, WorkspaceAccess.GetActor(User),
                "workspace", id.ToString(), $"v{version}, {reEncrypted} re-encrypted", ct);
            await Send.OkAsync(new Response(version, reEncrypted), ct);
        }
    }

    public sealed record Response(int Version, int ReEncrypted);
}

// Re-wrap every DEK under the current instance KEK (after a key change). Instance-admin only.
public static class RewrapKeys
{
    public sealed class Endpoint(KeyRotationService rotation, WorkspaceAccess access, IAuditSink audit)
        : EndpointWithoutRequest<Response>
    {
        public override void Configure()
        {
            Post("keys/rewrap");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (!access.IsInstanceAdmin(User))
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var count = await rotation.RewrapAllAsync(ct);
            await audit.RecordAsync(
                "key.rewrap", null, WorkspaceAccess.GetActor(User), "instance", null, $"{count} keys re-wrapped", ct);
            await Send.OkAsync(new Response(count), ct);
        }
    }

    public sealed record Response(int Rewrapped);
}
