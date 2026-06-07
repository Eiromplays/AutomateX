using AutomateX.Modules.Workspaces;
using Xunit;

namespace AutomateX.Tests;

// Rule encoded ahead of the implementation: a workspace must never lose its last
// Owner — removing or demoting the only Owner is refused.
public sealed class LastOwnerGuardTests
{
    private static WorkspaceMember Member(string email, WorkspaceRole role) =>
        WorkspaceMember.Create(Guid.Empty, email, role);

    [Fact]
    public void Removing_the_only_owner_is_refused()
    {
        var owner = Member("a@x", WorkspaceRole.Owner);
        var members = new[] { owner, Member("b@x", WorkspaceRole.Editor) };

        Assert.False(LastOwnerGuard.CanRemoveOrDemote(members, owner));
    }

    [Fact]
    public void Removing_an_owner_among_several_is_fine()
    {
        var owner = Member("a@x", WorkspaceRole.Owner);
        var members = new[] { owner, Member("b@x", WorkspaceRole.Owner) };

        Assert.True(LastOwnerGuard.CanRemoveOrDemote(members, owner));
    }

    [Fact]
    public void Removing_non_owners_is_always_fine()
    {
        var editor = Member("b@x", WorkspaceRole.Editor);
        var members = new[] { Member("a@x", WorkspaceRole.Owner), editor };

        Assert.True(LastOwnerGuard.CanRemoveOrDemote(members, editor));
    }
}
