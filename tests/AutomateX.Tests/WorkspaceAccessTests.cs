using System.Security.Claims;
using AutomateX.Database;
using AutomateX.Modules.Workspaces;
using AutomateX.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutomateX.Tests;

// Rules encoded ahead of the implementation:
// - open/apikey modes (no OIDC): everyone is Owner everywhere — workspaces are folders
// - OIDC mode, machine clients (unauthenticated principal passed the gate via key): Owner
// - a workspace with ZERO members is open to any authenticated user as Owner (bootstrap)
// - members match by bound subject first, then email (case-insensitive); first email
//   match binds the subject permanently
// - authenticated non-members get null (no access)
public sealed class WorkspaceAccessTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private static readonly AuthOptions OidcMode = new() { Authority = "https://login.example.com/v2.0", ClientId = "c" };

    private static ClaimsPrincipal User(string? subject, string? email) =>
        new(new ClaimsIdentity(
            new[]
                {
                    subject is null ? null : new Claim(ClaimTypes.NameIdentifier, subject),
                    email is null ? null : new Claim("preferred_username", email),
                }.Where(x => x is not null).Cast<Claim>(),
            "test"));

    private async Task<(WorkspaceAccess Access, AutomateXDbContext Db, IServiceScope Scope)> CreateAsync(AuthOptions options)
    {
        var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        return (new WorkspaceAccess(db, Options.Create(options)), db, scope);
    }

    [Fact]
    public async Task Open_mode_grants_owner_to_anyone()
    {
        var (access, _, scope) = await CreateAsync(new AuthOptions());
        using var _ = scope;

        var role = await access.GetRoleAsync(Workspace.DefaultId, new ClaimsPrincipal(), CancellationToken.None);

        Assert.Equal(WorkspaceRole.Owner, role);
    }

    [Fact]
    public async Task Oidc_mode_machine_clients_get_owner()
    {
        // An unauthenticated principal can only have passed the gate via X-Api-Key.
        var (access, _, scope) = await CreateAsync(OidcMode);
        using var _ = scope;

        var role = await access.GetRoleAsync(Workspace.DefaultId, new ClaimsPrincipal(new ClaimsIdentity()), CancellationToken.None);

        Assert.Equal(WorkspaceRole.Owner, role);
    }

    [Fact]
    public async Task Zero_member_workspace_is_open_to_any_authenticated_user()
    {
        var (access, db, scope) = await CreateAsync(OidcMode);
        using var _ = scope;
        var workspace = Workspace.Create($"empty-{Guid.CreateVersion7():N}");
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();

        var role = await access.GetRoleAsync(workspace.Id, User("sub-1", "a@b.c"), CancellationToken.None);

        Assert.Equal(WorkspaceRole.Owner, role);
    }

    [Fact]
    public async Task Members_get_their_role_and_email_match_binds_subject()
    {
        var (access, db, scope) = await CreateAsync(OidcMode);
        using var _ = scope;
        var workspace = Workspace.Create($"ws-{Guid.CreateVersion7():N}");
        var member = WorkspaceMember.Create(workspace.Id, "Eirik@Example.com", WorkspaceRole.Editor);
        db.Workspaces.Add(workspace);
        db.WorkspaceMembers.Add(member);
        await db.SaveChangesAsync();

        var role = await access.GetRoleAsync(workspace.Id, User("sub-42", "eirik@example.com"), CancellationToken.None);

        Assert.Equal(WorkspaceRole.Editor, role);
        db.ChangeTracker.Clear();
        var reloaded = await db.WorkspaceMembers.FindAsync(member.Id);
        Assert.Equal("sub-42", reloaded!.Subject);
    }

    [Fact]
    public void IsSelf_matches_bound_subject_or_email_case_insensitively()
    {
        var member = WorkspaceMember.Create(Guid.Empty, "Eirik@Example.com", WorkspaceRole.Viewer);

        Assert.True(WorkspaceAccess.IsSelf(member, User("any-sub", "EIRIK@example.COM")));
        Assert.False(WorkspaceAccess.IsSelf(member, User("any-sub", "other@example.com")));

        member.BindSubject("sub-7");
        Assert.True(WorkspaceAccess.IsSelf(member, User("sub-7", email: null)));
    }

    [Fact]
    public async Task Authenticated_non_members_get_no_access()
    {
        var (access, db, scope) = await CreateAsync(OidcMode);
        using var _ = scope;
        var workspace = Workspace.Create($"ws-{Guid.CreateVersion7():N}");
        db.Workspaces.Add(workspace);
        db.WorkspaceMembers.Add(WorkspaceMember.Create(workspace.Id, "owner@example.com", WorkspaceRole.Owner));
        await db.SaveChangesAsync();

        var role = await access.GetRoleAsync(workspace.Id, User("sub-x", "stranger@example.com"), CancellationToken.None);

        Assert.Null(role);
    }

    [Fact]
    public async Task Open_and_apikey_callers_are_instance_admins()
    {
        var (open, _, openScope) = await CreateAsync(new AuthOptions());
        using var _o = openScope;
        Assert.True(open.IsInstanceAdmin(new ClaimsPrincipal()));

        // OIDC mode, machine client (api-key through the gate → unauthenticated principal).
        var (oidc, _, oidcScope) = await CreateAsync(OidcMode);
        using var _a = oidcScope;
        Assert.True(oidc.IsInstanceAdmin(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Fact]
    public async Task Oidc_users_are_admins_only_when_listed()
    {
        var options = new AuthOptions
        {
            Authority = OidcMode.Authority,
            ClientId = OidcMode.ClientId,
            InstanceAdmins = ["sub-boss", "Admin@Corp.com"],
        };
        var (access, _, scope) = await CreateAsync(options);
        using var _ = scope;

        Assert.True(access.IsInstanceAdmin(User("sub-boss", "x@y.z"))); // by subject
        Assert.True(access.IsInstanceAdmin(User("other", "admin@corp.com"))); // by email, case-insensitive
        Assert.False(access.IsInstanceAdmin(User("nobody", "user@corp.com"))); // not listed
    }

    [Fact]
    public async Task Oidc_with_no_admins_configured_grants_no_one()
    {
        var (access, _, scope) = await CreateAsync(OidcMode);
        using var _ = scope;

        Assert.False(access.IsInstanceAdmin(User("sub-1", "user@corp.com")));
    }
}
