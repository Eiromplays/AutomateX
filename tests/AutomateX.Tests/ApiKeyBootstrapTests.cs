using AutomateX.Database;
using AutomateX.Modules.ApiKeys;
using AutomateX.Modules.Workspaces;
using AutomateX.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutomateX.Tests;

// Bootstrap mints one Instance admin key exactly when Auth__Bootstrap is set, OIDC is off, and the
// instance has no usable key. Otherwise it's a no-op, so open dev and OIDC-only stay untouched.
public sealed class ApiKeyBootstrapTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private async Task<(AutomateXDbContext Db, IServiceScope Scope)> FreshAsync()
    {
        var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        await db.ApiKeys.ExecuteDeleteAsync();
        return (db, scope);
    }

    [Fact]
    public async Task Mints_a_usable_instance_key_when_enabled_and_empty()
    {
        var (db, scope) = await FreshAsync();
        using var _ = scope;

        var token = await ApiKeyBootstrap.EnsureAsync(db, new AuthOptions { Bootstrap = true }, NullLogger.Instance);

        Assert.NotNull(token);
        var resolved = await new ApiKeyResolver(db, new ApiKeyPresence(), TimeProvider.System)
            .ResolveAsync(token!, CancellationToken.None);
        Assert.NotNull(resolved);
        Assert.Equal(ApiKeyScope.Instance, resolved!.Scope);
    }

    [Fact]
    public async Task Does_nothing_without_the_flag()
    {
        var (db, scope) = await FreshAsync();
        using var _ = scope;

        Assert.Null(await ApiKeyBootstrap.EnsureAsync(db, new AuthOptions { Bootstrap = false }, NullLogger.Instance));
        Assert.False(await db.ApiKeys.AnyAsync());
    }

    [Fact]
    public async Task Does_nothing_when_a_key_already_exists()
    {
        var (db, scope) = await FreshAsync();
        using var _ = scope;
        var (_, prefix, hash) = ApiKeyToken.Mint();
        db.ApiKeys.Add(ApiKey.Create("existing", prefix, hash, ApiKeyScope.Instance, null, null, WorkspaceRole.Owner, "t", null));
        await db.SaveChangesAsync();

        Assert.Null(await ApiKeyBootstrap.EnsureAsync(db, new AuthOptions { Bootstrap = true }, NullLogger.Instance));
        Assert.Equal(1, await db.ApiKeys.CountAsync());
    }

    [Fact]
    public async Task Does_nothing_when_oidc_is_configured()
    {
        var (db, scope) = await FreshAsync();
        using var _ = scope;

        var oidc = new AuthOptions { Bootstrap = true, Authority = "https://login.example.com/v2.0", ClientId = "c" };
        Assert.Null(await ApiKeyBootstrap.EnsureAsync(db, oidc, NullLogger.Instance));
        Assert.False(await db.ApiKeys.AnyAsync());
    }
}
