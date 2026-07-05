using AutomateX.Modules.ApiKeys;
using AutomateX.Modules.Workspaces;
using Xunit;

namespace AutomateX.Tests;

// Mint-scope authorization matrix (v4.6 m4): instance admins mint any scope; everyone else may mint only
// Workspace/Workflow keys, and only in a workspace they Own, at a role no higher than their own.
public sealed class ApiKeyMintPolicyTests
{
    [Theory]
    [InlineData(ApiKeyScope.Instance)]
    [InlineData(ApiKeyScope.Workspace)]
    [InlineData(ApiKeyScope.Workflow)]
    public void Instance_admin_can_mint_any_scope(ApiKeyScope scope)
    {
        Assert.True(ApiKeyMintPolicy.CanMint(scope, WorkspaceRole.Owner, isInstanceAdmin: true, callerWorkspaceRole: null));
    }

    [Fact]
    public void Non_admin_cannot_mint_instance_keys()
    {
        Assert.False(ApiKeyMintPolicy.CanMint(ApiKeyScope.Instance, WorkspaceRole.Owner, isInstanceAdmin: false, WorkspaceRole.Owner));
    }

    [Theory]
    [InlineData(ApiKeyScope.Workspace, WorkspaceRole.Viewer)]
    [InlineData(ApiKeyScope.Workspace, WorkspaceRole.Editor)]
    [InlineData(ApiKeyScope.Workspace, WorkspaceRole.Owner)]
    [InlineData(ApiKeyScope.Workflow, WorkspaceRole.Viewer)]
    [InlineData(ApiKeyScope.Workflow, WorkspaceRole.Owner)]
    public void Workspace_owner_can_mint_scoped_keys_up_to_owner(ApiKeyScope scope, WorkspaceRole role)
    {
        Assert.True(ApiKeyMintPolicy.CanMint(scope, role, isInstanceAdmin: false, WorkspaceRole.Owner));
    }

    [Theory]
    [InlineData(WorkspaceRole.Editor)]
    [InlineData(WorkspaceRole.Viewer)]
    public void Non_owners_cannot_mint_scoped_keys(WorkspaceRole callerRole)
    {
        Assert.False(ApiKeyMintPolicy.CanMint(ApiKeyScope.Workspace, WorkspaceRole.Viewer, isInstanceAdmin: false, callerRole));
        Assert.False(ApiKeyMintPolicy.CanMint(ApiKeyScope.Workflow, WorkspaceRole.Viewer, isInstanceAdmin: false, callerRole));
    }

    [Fact]
    public void No_role_in_the_target_workspace_cannot_mint()
    {
        Assert.False(ApiKeyMintPolicy.CanMint(ApiKeyScope.Workspace, WorkspaceRole.Viewer, isInstanceAdmin: false, callerWorkspaceRole: null));
    }
}
