using System.Security.Claims;
using AutomateX.Database;
using AutomateX.Modules.ApiKeys;
using AutomateX.Modules.Workspaces;
using AutomateX.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutomateX.Tests;

// WorkspaceAccess with an ApiKeyContext on HttpContext.Items. The key is authoritative and never touches
// the DB: an Instance key is Owner everywhere + instance admin; a scoped key is its Role but only within
// its own workspace, and an X-Workspace-Id header can never widen it. Actor becomes "key:<name>".
public sealed class ScopedWorkspaceAccessTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private static readonly AuthOptions OidcMode = new() { Authority = "https://login.example.com/v2.0", ClientId = "c" };

    private WorkspaceAccess Access(AuthOptions options, out IServiceScope scope)
    {
        scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        return new WorkspaceAccess(db, Options.Create(options));
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
    public async Task Instance_key_is_owner_for_any_workspace_and_instance_admin()
    {
        var access = Access(OidcMode, out var scope);
        using var _ = scope;
        var key = new ApiKeyContext(ApiKeyScope.Instance, null, null, WorkspaceRole.Owner, "key:boot");
        var target = Guid.NewGuid();

        Assert.Equal(target, await access.AuthorizeAsync(WithKey(key, target), WorkspaceRole.Owner, CancellationToken.None));
        Assert.Equal(WorkspaceRole.Owner, await access.GetRoleAsync(target, WithKey(key), CancellationToken.None));
        Assert.True(access.IsInstanceAdmin(WithKey(key)));
        Assert.Equal("key:boot", WorkspaceAccess.GetActor(WithKey(key)));

        // No header → acts on the default workspace.
        Assert.Equal(
            Workspace.DefaultId,
            await access.AuthorizeAsync(WithKey(key), WorkspaceRole.Owner, CancellationToken.None));
    }

    [Fact]
    public async Task Workspace_key_grants_its_role_within_its_workspace_only()
    {
        var access = Access(OidcMode, out var scope);
        using var _ = scope;
        var ws = Guid.NewGuid();
        var key = new ApiKeyContext(ApiKeyScope.Workspace, ws, null, WorkspaceRole.Editor, "key:ci");

        Assert.Equal(ws, await access.AuthorizeAsync(WithKey(key), WorkspaceRole.Editor, CancellationToken.None));
        Assert.Null(await access.AuthorizeAsync(WithKey(key), WorkspaceRole.Owner, CancellationToken.None));
        Assert.Equal(WorkspaceRole.Editor, await access.GetRoleAsync(ws, WithKey(key), CancellationToken.None));
        Assert.Null(await access.GetRoleAsync(Guid.NewGuid(), WithKey(key), CancellationToken.None));
        Assert.False(access.IsInstanceAdmin(WithKey(key)));
    }

    [Fact]
    public async Task Workspace_key_header_cannot_widen_to_another_workspace()
    {
        var access = Access(OidcMode, out var scope);
        using var _ = scope;
        var ws = Guid.NewGuid();
        var key = new ApiKeyContext(ApiKeyScope.Workspace, ws, null, WorkspaceRole.Owner, "key:ws");

        Assert.Null(await access.AuthorizeAsync(WithKey(key, Guid.NewGuid()), WorkspaceRole.Viewer, CancellationToken.None));
        Assert.Equal(ws, await access.AuthorizeAsync(WithKey(key, ws), WorkspaceRole.Owner, CancellationToken.None));
    }

    [Fact]
    public async Task Workflow_key_is_workspace_bound_like_a_workspace_key()
    {
        var access = Access(OidcMode, out var scope);
        using var _ = scope;
        var ws = Guid.NewGuid();
        var key = new ApiKeyContext(ApiKeyScope.Workflow, ws, Guid.NewGuid(), WorkspaceRole.Editor, "key:wf");

        Assert.Equal(WorkspaceRole.Editor, await access.GetRoleAsync(ws, WithKey(key), CancellationToken.None));
        Assert.Null(await access.GetRoleAsync(Guid.NewGuid(), WithKey(key), CancellationToken.None));
        Assert.Null(await access.AuthorizeAsync(WithKey(key, Guid.NewGuid()), WorkspaceRole.Viewer, CancellationToken.None));
        Assert.False(access.IsInstanceAdmin(WithKey(key)));
    }

    [Fact]
    public void Without_a_key_context_actor_and_admin_defer_to_the_principal()
    {
        var open = Access(new AuthOptions(), out var openScope);
        using var _o = openScope;
        var anonymous = new DefaultHttpContext();
        Assert.Equal("api-key", WorkspaceAccess.GetActor(anonymous));
        Assert.True(open.IsInstanceAdmin(anonymous));

        var oidc = Access(OidcMode, out var oidcScope);
        using var _a = oidcScope;
        var signedIn = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "sub-9")], "test")),
        };
        Assert.Equal("sub-9", WorkspaceAccess.GetActor(signedIn));
        Assert.False(oidc.IsInstanceAdmin(signedIn));
    }
}
