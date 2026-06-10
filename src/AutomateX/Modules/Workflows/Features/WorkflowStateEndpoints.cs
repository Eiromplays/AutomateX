using AutomateX.Database;
using AutomateX.Modules.State;
using AutomateX.Modules.Workspaces;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Workflows.Features;

// Surfaces the durable per-workflow KV state (feed dedup, cursors, …) so it can be
// inspected, and cleared to make a feed re-process from scratch.
public static class GetWorkflowState
{
    public sealed record StateEntry(string Key, string Value, DateTimeOffset? ExpiresAt, DateTimeOffset UpdatedAt);

    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access, IWorkflowStateStore state)
        : EndpointWithoutRequest<List<StateEntry>>
    {
        public override void Configure()
        {
            Get("workflows/{id}/state");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Viewer, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var id = Route<Guid>("id");
            if (!await dbContext.Workflows.AnyAsync(x => x.Id == id && x.WorkspaceId == ws, ct))
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            var items = await state.ListByPrefixAsync(id, "", ct);
            await Send.OkAsync(
                items.Select(x => new StateEntry(x.Key, x.Value, x.ExpiresAt, x.UpdatedAt)).ToList(), ct);
        }
    }
}

public static class ClearWorkflowState
{
    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access, IWorkflowStateStore state)
        : EndpointWithoutRequest
    {
        public override void Configure()
        {
            Delete("workflows/{id}/state");
            AllowAnonymous();
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Editor, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var id = Route<Guid>("id");
            if (!await dbContext.Workflows.AnyAsync(x => x.Id == id && x.WorkspaceId == ws, ct))
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            // ?prefix=trigger:<id>: clears just that namespace; absent clears everything.
            var prefix = HttpContext.Request.Query["prefix"].ToString();
            if (string.IsNullOrEmpty(prefix))
            {
                await state.ClearAsync(id, ct);
            }
            else
            {
                await state.ClearByPrefixAsync(id, prefix, ct);
            }

            await Send.NoContentAsync(ct);
        }
    }
}

// Overwrite (or create) a single state entry's value. The key carries the full
// namespaced name; values are plaintext, so it's an admin/debug edit — Editor only.
public static class SetWorkflowStateEntry
{
    public sealed record Request(string Key, string Value);

    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access, IWorkflowStateStore state)
        : Endpoint<Request>
    {
        public override void Configure()
        {
            Put("workflows/{id}/state");
            AllowAnonymous();
        }

        public override async Task HandleAsync(Request req, CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Editor, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var id = Route<Guid>("id");
            if (!await dbContext.Workflows.AnyAsync(x => x.Id == id && x.WorkspaceId == ws, ct))
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            if (string.IsNullOrEmpty(req.Key))
            {
                ThrowError("key is required.");
                return;
            }

            await state.SetAsync(id, req.Key, req.Value, cancellationToken: ct);
            await Send.NoContentAsync(ct);
        }
    }
}

// Remove a single state entry (e.g. drop one feed item so it re-processes).
public static class RemoveWorkflowStateEntry
{
    public sealed record Request(string Key);

    public sealed class Endpoint(AutomateXDbContext dbContext, WorkspaceAccess access, IWorkflowStateStore state)
        : Endpoint<Request>
    {
        public override void Configure()
        {
            Delete("workflows/{id}/state/entry");
            AllowAnonymous();
        }

        public override async Task HandleAsync(Request req, CancellationToken ct)
        {
            if (await access.AuthorizeAsync(HttpContext, WorkspaceRole.Editor, ct) is not { } ws)
            {
                await Send.ForbiddenAsync(ct);
                return;
            }

            var id = Route<Guid>("id");
            if (!await dbContext.Workflows.AnyAsync(x => x.Id == id && x.WorkspaceId == ws, ct))
            {
                await Send.NotFoundAsync(ct);
                return;
            }

            await state.RemoveAsync(id, req.Key, ct);
            await Send.NoContentAsync(ct);
        }
    }
}
