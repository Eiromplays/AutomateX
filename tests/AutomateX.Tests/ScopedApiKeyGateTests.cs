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

// The gate wired to a real ApiKeyResolver: an atx_ token is resolved against the DB and, on a hit, its
// ApiKeyContext is attached to HttpContext for WorkspaceAccess to read; a miss is a hard 401. The instance
// engages once any DB key exists (via ApiKeyPresence), even with neither a static key nor OIDC configured.
public sealed class ScopedApiKeyGateTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private static readonly AuthOptions Open = new();
    private static readonly AuthOptions OidcOnly = new() { Authority = "https://login.example.com/v2.0", ClientId = "c" };

    private static (string Token, ApiKey Key) NewKey(
        string name, ApiKeyScope scope, Guid? workspaceId = null, WorkspaceRole role = WorkspaceRole.Editor)
    {
        var (token, prefix, hash) = ApiKeyToken.Mint();
        return (token, ApiKey.Create(name, prefix, hash, scope, workspaceId, null, role, "test", null));
    }

    private static async Task<(int Status, bool NextCalled, HttpContext Context)> RunAsync(
        AutomateXDbContext db,
        AuthOptions options,
        string path,
        string? headerKey = null,
        string? cookieKey = null,
        bool authenticatedUser = false,
        Action<ApiKeyPresence>? seedPresence = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<ApiKeyPresence>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ApiKeyResolver>();
        var provider = services.BuildServiceProvider();
        seedPresence?.Invoke(provider.GetRequiredService<ApiKeyPresence>());

        var nextCalled = false;
        var middleware = new AuthGateMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            Options.Create(options));

        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Path = path;
        if (headerKey is not null)
        {
            context.Request.Headers["X-Api-Key"] = headerKey;
        }

        if (cookieKey is not null)
        {
            context.Request.Headers.Cookie = $"{AuthGateMiddleware.CookieName}={cookieKey}";
        }

        if (authenticatedUser)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity("test"));
        }

        await middleware.InvokeAsync(context);
        return (context.Response.StatusCode, nextCalled, context);
    }

    private (AutomateXDbContext Db, IServiceScope Scope) Db()
    {
        var scope = fixture.Host.Services.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<AutomateXDbContext>(), scope);
    }

    [Fact]
    public async Task Instance_key_in_header_passes_and_attaches_context()
    {
        var (db, scope) = Db();
        using var _ = scope;
        var (token, key) = NewKey("boot", ApiKeyScope.Instance, role: WorkspaceRole.Owner);
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync();

        var (status, next, context) = await RunAsync(db, Open, "/api/workflows", headerKey: token);

        Assert.True(next);
        Assert.Equal(StatusCodes.Status200OK, status);
        var resolved = ApiKeyContext.From(context);
        Assert.NotNull(resolved);
        Assert.Equal(ApiKeyScope.Instance, resolved!.Scope);
        Assert.Equal("key:boot", resolved.Actor);
    }

    [Fact]
    public async Task Workspace_key_via_cookie_passes_on_hub_path()
    {
        var (db, scope) = Db();
        using var _ = scope;
        var workspaceId = Guid.NewGuid();
        var (token, key) = NewKey("grafana", ApiKeyScope.Workspace, workspaceId, WorkspaceRole.Viewer);
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync();

        var (_, next, context) = await RunAsync(db, Open, "/hubs/executions", cookieKey: token);

        Assert.True(next);
        var resolved = ApiKeyContext.From(context);
        Assert.Equal(ApiKeyScope.Workspace, resolved!.Scope);
        Assert.Equal(workspaceId, resolved.WorkspaceId);
        Assert.Equal(WorkspaceRole.Viewer, resolved.Role);
    }

    [Fact]
    public async Task Unknown_atx_token_is_401_and_does_not_fall_back()
    {
        var (db, scope) = Db();
        using var _ = scope;

        // OIDC engages the gate; the bad atx_ token must still be rejected outright, not fall through to OIDC.
        var (status, next, _) = await RunAsync(db, OidcOnly, "/api/workflows", headerKey: "atx_deadbeef_nope");

        Assert.False(next);
        Assert.Equal(StatusCodes.Status401Unauthorized, status);
    }

    [Fact]
    public async Task Revoked_key_is_401()
    {
        var (db, scope) = Db();
        using var _ = scope;
        var (token, key) = NewKey("old", ApiKeyScope.Instance, role: WorkspaceRole.Owner);
        key.Revoke();
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync();

        var (status, next, _) = await RunAsync(db, OidcOnly, "/api/workflows", headerKey: token);

        Assert.False(next);
        Assert.Equal(StatusCodes.Status401Unauthorized, status);
    }

    [Fact]
    public async Task Db_key_present_engages_the_gate_so_anonymous_is_401()
    {
        var (db, scope) = Db();
        using var _ = scope;
        var (_, key) = NewKey("live", ApiKeyScope.Instance, role: WorkspaceRole.Owner);
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync();

        // No static key, no OIDC, no credentials — but a live DB key means auth is engaged.
        var (status, next, _) = await RunAsync(db, Open, "/api/workflows");

        Assert.False(next);
        Assert.Equal(StatusCodes.Status401Unauthorized, status);
    }

    [Fact]
    public async Task Open_with_no_keys_stays_open()
    {
        var (db, scope) = Db();
        using var _ = scope;

        // Force "no keys" so the assertion is independent of rows other tests left in the shared DB.
        var (status, next, _) = await RunAsync(db, Open, "/api/workflows", seedPresence: p => p.Remember(false));

        Assert.True(next);
        Assert.Equal(StatusCodes.Status200OK, status);
    }
}
