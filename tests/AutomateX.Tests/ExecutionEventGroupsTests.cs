using AutomateX.Web;
using Xunit;

namespace AutomateX.Tests;

public sealed class ExecutionEventGroupsTests
{
    [Fact]
    public void Group_name_is_stable_and_workspace_scoped()
    {
        var id = Guid.Parse("0199c986-0000-7000-8000-000000000000");

        Assert.Equal("workspace:0199c986-0000-7000-8000-000000000000", ExecutionEventGroups.ForWorkspace(id));
        Assert.NotEqual(ExecutionEventGroups.ForWorkspace(id), ExecutionEventGroups.ForWorkspace(Guid.CreateVersion7()));
    }
}
