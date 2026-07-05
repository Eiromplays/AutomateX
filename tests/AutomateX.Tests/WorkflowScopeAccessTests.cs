using AutomateX.Database;
using AutomateX.Modules.ApiKeys;
using AutomateX.Modules.Workspaces;
using AutomateX.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutomateX.Tests;

// Workflow-scope enforcement (v4.6 m3): a Workflow-scoped key is confined to its one workflow.
// AuthorizeWorkflowAsync gates the workflow-addressed endpoints; ScopedWorkflowId folds into the
// list/entity queries. Any other caller (Instance/Workspace key, OIDC, open) reaches every workflow
// in its workspace. OIDC mode is used so the key path is proven to short-circuit membership.
public sealed class WorkflowScopeAccessTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private static readonly AuthOptions OidcMode = new() { Authority = "https://login.example.com/v2.0", ClientId = "c" };

    private WorkspaceAccess Access(out IServiceScope scope)
    {
        scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        return new WorkspaceAccess(db, Options.Create(OidcMode));
    }

    private static DefaultHttpContext WithKey(ApiKeyContext key, Guid? headerWorkspace = null)
    {
        var context = new DefaultHttpContext();
        if (headerWorkspace is { } ws)
        {
            context.Request.Headers[WorkspaceHttp.HeaderName] = ws.ToString();
        }

        ApiKeyContext.Set(context, key);
        return context;
    }

    [Fact]
    public async Task Workflow_key_authorizes_only_its_own_workflow_at_its_role()
    {
        var access = Access(out var scope);
        using var _ = scope;
        var ws = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var key = new ApiKeyContext(ApiKeyScope.Workflow, ws, workflowId, WorkspaceRole.Editor, "key:runner");

        Assert.Equal(ws, await access.AuthorizeWorkflowAsync(WithKey(key), workflowId, WorkspaceRole.Editor, CancellationToken.None));
        Assert.Null(await access.AuthorizeWorkflowAsync(WithKey(key), Guid.NewGuid(), WorkspaceRole.Editor, CancellationToken.None));
        Assert.Null(await access.AuthorizeWorkflowAsync(WithKey(key), workflowId, WorkspaceRole.Owner, CancellationToken.None));
    }

    [Fact]
    public async Task Workspace_and_instance_keys_authorize_any_workflow()
    {
        var access = Access(out var scope);
        using var _ = scope;
        var ws = Guid.NewGuid();

        var workspaceKey = new ApiKeyContext(ApiKeyScope.Workspace, ws, null, WorkspaceRole.Editor, "key:ws");
        Assert.Equal(
            ws,
            await access.AuthorizeWorkflowAsync(WithKey(workspaceKey), Guid.NewGuid(), WorkspaceRole.Editor, CancellationToken.None));

        var instanceKey = new ApiKeyContext(ApiKeyScope.Instance, null, null, WorkspaceRole.Owner, "key:boot");
        Assert.Equal(
            Workspace.DefaultId,
            await access.AuthorizeWorkflowAsync(WithKey(instanceKey), Guid.NewGuid(), WorkspaceRole.Owner, CancellationToken.None));
    }

    [Fact]
    public async Task Workflow_key_with_a_mismatched_workspace_header_is_refused()
    {
        var access = Access(out var scope);
        using var _ = scope;
        var ws = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var key = new ApiKeyContext(ApiKeyScope.Workflow, ws, workflowId, WorkspaceRole.Editor, "key:runner");

        var context = WithKey(key, headerWorkspace: Guid.NewGuid());

        Assert.Null(await access.AuthorizeWorkflowAsync(context, workflowId, WorkspaceRole.Editor, CancellationToken.None));
    }

    [Fact]
    public void ScopedWorkflowId_is_set_only_for_workflow_keys()
    {
        var ws = Guid.NewGuid();
        var workflowId = Guid.NewGuid();

        Assert.Equal(
            workflowId,
            WorkspaceAccess.ScopedWorkflowId(WithKey(new ApiKeyContext(ApiKeyScope.Workflow, ws, workflowId, WorkspaceRole.Viewer, "key:wf"))));
        Assert.Null(WorkspaceAccess.ScopedWorkflowId(WithKey(new ApiKeyContext(ApiKeyScope.Workspace, ws, null, WorkspaceRole.Viewer, "key:ws"))));
        Assert.Null(WorkspaceAccess.ScopedWorkflowId(WithKey(new ApiKeyContext(ApiKeyScope.Instance, null, null, WorkspaceRole.Owner, "key:boot"))));
        Assert.Null(WorkspaceAccess.ScopedWorkflowId(new DefaultHttpContext()));
    }
}
