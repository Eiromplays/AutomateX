using AutomateX.Database;
using AutomateX.Modules.ApiKeys;
using AutomateX.Modules.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutomateX.Tests;

// Resolution rules: parse atx_<prefix>_<secret>, look up by the unique Prefix, fixed-time compare the
// secret hash, reject revoked/expired, stamp LastUsedAt at most once a minute; a valid token yields the
// key's ApiKeyContext. HasActiveKeysAsync backs the gate's engagement check and caches via ApiKeyPresence.
public sealed class ApiKeyResolverTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private (ApiKeyResolver Resolver, ApiKeyPresence Presence, AutomateXDbContext Db, IServiceScope Scope) Create()
    {
        var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        var presence = new ApiKeyPresence();
        return (new ApiKeyResolver(db, presence, TimeProvider.System), presence, db, scope);
    }

    private static (string Token, ApiKey Key) NewKey(
        string name,
        ApiKeyScope scope,
        Guid? workspaceId = null,
        Guid? workflowId = null,
        WorkspaceRole role = WorkspaceRole.Editor,
        DateTimeOffset? expiresAt = null)
    {
        var (token, prefix, hash) = ApiKeyToken.Mint();
        return (token, ApiKey.Create(name, prefix, hash, scope, workspaceId, workflowId, role, "test", expiresAt));
    }

    [Fact]
    public async Task Valid_token_resolves_to_its_scope_workspace_role_and_actor()
    {
        var (resolver, _, db, scope) = Create();
        using var _ = scope;
        var workspaceId = Guid.NewGuid();
        var (token, key) = NewKey("grafana-ro", ApiKeyScope.Workspace, workspaceId, role: WorkspaceRole.Viewer);
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync();

        var context = await resolver.ResolveAsync(token, CancellationToken.None);

        Assert.NotNull(context);
        Assert.Equal(ApiKeyScope.Workspace, context!.Scope);
        Assert.Equal(workspaceId, context.WorkspaceId);
        Assert.Equal(WorkspaceRole.Viewer, context.Role);
        Assert.Equal("key:grafana-ro", context.Actor);
    }

    [Fact]
    public async Task Wrong_secret_does_not_resolve()
    {
        var (resolver, _, db, scope) = Create();
        using var _ = scope;
        var (_, key) = NewKey("k", ApiKeyScope.Instance, role: WorkspaceRole.Owner);
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync();

        // Correct prefix, wrong secret half.
        Assert.Null(await resolver.ResolveAsync($"atx_{key.Prefix}_deadbeef", CancellationToken.None));
    }

    [Fact]
    public async Task Unknown_prefix_does_not_resolve()
    {
        var (resolver, _, _, scope) = Create();
        using var _ = scope;
        var (token, _) = NewKey("never-stored", ApiKeyScope.Instance, role: WorkspaceRole.Owner);

        Assert.Null(await resolver.ResolveAsync(token, CancellationToken.None));
    }

    [Fact]
    public async Task Malformed_token_does_not_resolve()
    {
        var (resolver, _, _, scope) = Create();
        using var _ = scope;

        Assert.Null(await resolver.ResolveAsync("not-a-token", CancellationToken.None));
    }

    [Fact]
    public async Task Revoked_key_does_not_resolve()
    {
        var (resolver, _, db, scope) = Create();
        using var _ = scope;
        var (token, key) = NewKey("revoked", ApiKeyScope.Instance, role: WorkspaceRole.Owner);
        key.Revoke();
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync();

        Assert.Null(await resolver.ResolveAsync(token, CancellationToken.None));
    }

    [Fact]
    public async Task Expired_key_does_not_resolve()
    {
        var (resolver, _, db, scope) = Create();
        using var _ = scope;
        var (token, key) = NewKey(
            "expired", ApiKeyScope.Instance, role: WorkspaceRole.Owner, expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync();

        Assert.Null(await resolver.ResolveAsync(token, CancellationToken.None));
    }

    [Fact]
    public async Task LastUsedAt_is_stamped_once_then_throttled()
    {
        var (resolver, _, db, scope) = Create();
        using var _ = scope;
        var (token, key) = NewKey("hot", ApiKeyScope.Instance, role: WorkspaceRole.Owner);
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync();

        Assert.NotNull(await resolver.ResolveAsync(token, CancellationToken.None));
        var firstStamp = key.LastUsedAt;
        Assert.NotNull(firstStamp);

        Assert.NotNull(await resolver.ResolveAsync(token, CancellationToken.None));
        Assert.Equal(firstStamp, key.LastUsedAt); // within the window → no second write
    }

    [Fact]
    public async Task Resolve_marks_presence_present()
    {
        var (resolver, presence, db, scope) = Create();
        using var _ = scope;
        var (token, key) = NewKey("live", ApiKeyScope.Instance, role: WorkspaceRole.Owner);
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync();

        await resolver.ResolveAsync(token, CancellationToken.None);

        Assert.Equal(true, presence.Cached);
    }

    [Fact]
    public async Task HasActiveKeys_is_true_when_a_live_key_exists()
    {
        var (resolver, presence, db, scope) = Create();
        using var _ = scope;
        var (_, key) = NewKey("live", ApiKeyScope.Instance, role: WorkspaceRole.Owner);
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync();

        Assert.True(await resolver.HasActiveKeysAsync(CancellationToken.None));
        Assert.Equal(true, presence.Cached);
    }

    [Fact]
    public async Task HasActiveKeys_returns_the_cached_answer_without_querying()
    {
        var (resolver, presence, db, scope) = Create();
        using var _ = scope;
        var (_, key) = NewKey("live", ApiKeyScope.Instance, role: WorkspaceRole.Owner);
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync();

        presence.Remember(false); // force a stale "no keys"

        Assert.False(await resolver.HasActiveKeysAsync(CancellationToken.None));
    }

    [Fact]
    public void Presence_tracks_known_state()
    {
        var presence = new ApiKeyPresence();

        Assert.Null(presence.Cached);

        presence.Remember(true);
        Assert.Equal(true, presence.Cached);

        presence.Invalidate();
        Assert.Null(presence.Cached);

        presence.Remember(false);
        Assert.Equal(false, presence.Cached);
    }
}
