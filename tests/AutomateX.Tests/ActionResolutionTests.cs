using AutomateX.Engine.Actions;
using Xunit;

namespace AutomateX.Tests;

// The workspace-plugin resolution rules: a workspace's catalog is global ∪ its own
// plugins; its own shadow global on collision — for that workspace only.
public sealed class ActionResolutionTests
{
    private sealed class FakeExecutor(string type) : IActionExecutor
    {
        public string ActionType => type;

        public Task<string?> ExecuteAsync(string configJson, ActionInvocation invocation, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>("ok");
    }

    private static RegisteredAction Action(string type, string source) =>
        new(new ActionDescriptor(type, type, null, source, null, null), new FakeExecutor(type));

    private static readonly Guid WorkspaceA = Guid.CreateVersion7();
    private static readonly Guid WorkspaceB = Guid.CreateVersion7();

    private static readonly RegisteredAction GlobalHttp = Action("http.request", "builtin");
    private static readonly RegisteredAction GlobalMatrix = Action("matrix.send", "plugin:Matrix");
    private static readonly RegisteredAction WorkspaceMatrix = Action("matrix.send", "workspace:Matrix");
    private static readonly RegisteredAction WorkspaceCustom = Action("custom.thing", "workspace:Custom");

    private static ActionSnapshot Build() => ActionSnapshot.Compose(
        [GlobalHttp, GlobalMatrix],
        new Dictionary<Guid, IReadOnlyList<RegisteredAction>>
        {
            [WorkspaceA] = [WorkspaceMatrix, WorkspaceCustom],
        });

    [Fact]
    public void Workspace_actions_shadow_global_for_their_workspace_only()
    {
        var snapshot = Build();

        Assert.Same(WorkspaceMatrix.Executor, snapshot.Get("matrix.send", WorkspaceA));
        Assert.Same(GlobalMatrix.Executor, snapshot.Get("matrix.send", WorkspaceB));
    }

    [Fact]
    public void Workspace_actions_are_invisible_to_other_workspaces()
    {
        var snapshot = Build();

        Assert.True(snapshot.Contains("custom.thing", WorkspaceA));
        Assert.False(snapshot.Contains("custom.thing", WorkspaceB));

        var exception = Assert.Throws<InvalidOperationException>(() => snapshot.Get("custom.thing", WorkspaceB));
        Assert.Contains("custom.thing", exception.Message);
    }

    [Fact]
    public void Global_actions_are_available_everywhere()
    {
        var snapshot = Build();

        Assert.Same(GlobalHttp.Executor, snapshot.Get("http.request", WorkspaceA));
        Assert.Same(GlobalHttp.Executor, snapshot.Get("http.request", WorkspaceB));
        Assert.True(snapshot.Contains("http.request", Guid.CreateVersion7()));
    }

    [Fact]
    public void Unknown_action_throws_with_the_type_name()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Build().Get("nope.nothing", WorkspaceA));
        Assert.Contains("nope.nothing", exception.Message);
    }

    [Fact]
    public void Descriptors_are_the_union_with_workspace_winning_collisions()
    {
        var snapshot = Build();

        var forA = snapshot.Descriptors(WorkspaceA);
        Assert.Equal(3, forA.Count);
        Assert.Equal("workspace:Matrix", forA.Single(x => x.Type == "matrix.send").Source);

        var forB = snapshot.Descriptors(WorkspaceB);
        Assert.Equal(2, forB.Count);
        Assert.Equal("plugin:Matrix", forB.Single(x => x.Type == "matrix.send").Source);
    }
}
