using System.Security.Claims;
using AutomateX.Database;
using AutomateX.Modules.ApiKeys;
using AutomateX.Modules.Workspaces;
using AutomateX.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutomateX.Tests;

// v4.9 m2: the plugin-log stream is an instance-admin ops view. JoinPlugin must add a connection to the
// plugin's group only when the handshake principal is an instance admin — mirroring GetPluginLogs. Auth
// is exercised through a real WorkspaceAccess (OIDC mode) keyed by the same ApiKeyContext the gate sets.
public sealed class PluginLogsHubTests(EngineFixture fixture) : IClassFixture<EngineFixture>
{
    private static readonly AuthOptions OidcMode = new() { Authority = "https://login.example.com/v2.0", ClientId = "c" };

    [Fact]
    public async Task JoinPlugin_declines_a_non_admin_connection()
    {
        var (hub, groups) = Hub(new ApiKeyContext(ApiKeyScope.Workspace, Guid.NewGuid(), null, WorkspaceRole.Owner, "key:ws"));

        await hub.JoinPlugin("acme");

        Assert.Empty(groups.Added);
    }

    [Fact]
    public async Task JoinPlugin_adds_an_instance_admin_to_the_plugin_group()
    {
        var (hub, groups) = Hub(new ApiKeyContext(ApiKeyScope.Instance, null, null, WorkspaceRole.Owner, "key:boot"));

        await hub.JoinPlugin("acme");

        Assert.Equal(("conn-1", "plugin:acme"), Assert.Single(groups.Added));
    }

    private (PluginLogsHub Hub, RecordingGroupManager Groups) Hub(ApiKeyContext key)
    {
        var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutomateXDbContext>();
        var access = new WorkspaceAccess(db, Options.Create(OidcMode));

        var http = new DefaultHttpContext();
        ApiKeyContext.Set(http, key);

        var groups = new RecordingGroupManager();
        var hub = new PluginLogsHub(access)
        {
            Context = new FakeHubCallerContext(http),
            Groups = groups,
        };
        return (hub, groups);
    }

    private sealed class RecordingGroupManager : IGroupManager
    {
        public List<(string ConnectionId, string Group)> Added { get; } = [];

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Added.Add((connectionId, groupName));
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeHubCallerContext : HubCallerContext
    {
        private readonly IFeatureCollection _features = new FeatureCollection();

        public FakeHubCallerContext(HttpContext http) =>
            _features.Set<IHttpContextFeature>(new HttpContextHolder { HttpContext = http });

        public override string ConnectionId => "conn-1";

        public override string? UserIdentifier => null;

        public override ClaimsPrincipal? User => null;

        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

        public override IFeatureCollection Features => _features;

        public override CancellationToken ConnectionAborted => CancellationToken.None;

        public override void Abort()
        {
        }

        private sealed class HttpContextHolder : IHttpContextFeature
        {
            public HttpContext? HttpContext { get; set; }
        }
    }
}
