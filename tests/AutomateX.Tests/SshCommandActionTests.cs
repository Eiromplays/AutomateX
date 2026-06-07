using AutomateX.Engine.Actions;
using AutomateX.Plugin.Sdk;
using AutomateX.Plugins.Ssh;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Renci.SshNet.Common;
using Xunit;

namespace AutomateX.Tests;

// A real throwaway SSH server — the same testing posture as the engine's Postgres.
public sealed class SshServerFixture : IAsyncLifetime
{
    public const string Password = "automatex-test-pw";

    public const string AuthorizedPublicKey =
        "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIHD9VGqva688+HKLvODNd0Y+9mhkwFGihcG9pggQPxzc automatex-tests";

    public const string PrivateKey =
        """
        -----BEGIN OPENSSH PRIVATE KEY-----
        b3BlbnNzaC1rZXktdjEAAAAABG5vbmUAAAAEbm9uZQAAAAAAAAABAAAAMwAAAAtzc2gtZW
        QyNTUxOQAAACBw/VRqr2uvPPhyi7zgzXdGPvZoZMBRooXBvaYIED8c3AAAAJiLzeWci83l
        nAAAAAtzc2gtZWQyNTUxOQAAACBw/VRqr2uvPPhyi7zgzXdGPvZoZMBRooXBvaYIED8c3A
        AAAEB3SH4RK6Nc+TIEQJrQI4ADkFpA4pvFYwzhvGBgm1suWnD9VGqva688+HKLvODNd0Y+
        9mhkwFGihcG9pggQPxzcAAAAD2F1dG9tYXRleC10ZXN0cwECAwQFBg==
        -----END OPENSSH PRIVATE KEY-----
        """;

    private readonly IContainer _container = new ContainerBuilder("testcontainers/sshd:1.3.0")
        .WithEnvironment("PASSWORD", Password)
        .WithPortBinding(22, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(22))
        .Build();

    public string Host => _container.Hostname;

    public int Port => _container.GetMappedPublicPort(22);

    public string HostKeyFingerprint { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await _container.ExecAsync(["sh", "-c",
            $"mkdir -p /root/.ssh && echo '{AuthorizedPublicKey}' > /root/.ssh/authorized_keys"
            + " && chmod 700 /root/.ssh && chmod 600 /root/.ssh/authorized_keys"]);

        // "256 SHA256:<base64> root@host (ED25519)" — the same format users get from ssh-keygen -lf.
        var result = await _container.ExecAsync(["sh", "-c", "ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub"]);
        HostKeyFingerprint = result.Stdout.Split(' ')[1].Trim();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

public sealed class SshCommandActionTests(SshServerFixture server) : IClassFixture<SshServerFixture>
{
    private static ActionContext Context() => new()
    {
        Logger = NullLogger.Instance,
        Http = new HttpClient(),
        ExecutionId = Guid.CreateVersion7(),
        WorkflowId = Guid.CreateVersion7(),
        StepOrder = 0,
    };

    private SshCommandConfig Config(string command) => new(
        Host: server.Host,
        Username: "root",
        Command: command,
        Port: server.Port,
        Password: SshServerFixture.Password);

    [Fact]
    public async Task Password_auth_executes_and_captures_streams()
    {
        var result = await new SshCommandAction().ExecuteAsync(
            Config("echo out-marker && echo err-marker 1>&2"), Context());

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("out-marker", result.Stdout);
        Assert.Contains("err-marker", result.Stderr);
    }

    [Fact]
    public async Task Private_key_auth_executes()
    {
        var config = Config("echo key-auth-ok") with { Password = null, PrivateKey = SshServerFixture.PrivateKey };

        var result = await new SshCommandAction().ExecuteAsync(config, Context());

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("key-auth-ok", result.Stdout);
    }

    [Fact]
    public async Task Non_zero_exit_fails_the_step_with_code_and_stderr()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SshCommandAction().ExecuteAsync(Config("echo boom 1>&2; exit 7"), Context()));

        Assert.Contains("7", exception.Message);
        Assert.Contains("boom", exception.Message);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        var config = Config("echo never") with { Password = "wrong-password" };

        await Assert.ThrowsAsync<SshAuthenticationException>(
            () => new SshCommandAction().ExecuteAsync(config, Context()));
    }

    [Fact]
    public async Task Mismatched_host_fingerprint_refuses_connection()
    {
        var config = Config("echo never") with
        {
            HostFingerprint = "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
        };

        await Assert.ThrowsAsync<SshConnectionException>(
            () => new SshCommandAction().ExecuteAsync(config, Context()));
    }

    [Fact]
    public async Task Matching_host_fingerprint_connects()
    {
        var config = Config("echo pinned-ok") with { HostFingerprint = server.HostKeyFingerprint };

        var result = await new SshCommandAction().ExecuteAsync(config, Context());

        Assert.Contains("pinned-ok", result.Stdout);
    }

    [Fact]
    public async Task Command_timeout_cancels()
    {
        var config = Config("sleep 30") with { TimeoutSeconds = 2 };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new SshCommandAction().ExecuteAsync(config, Context()));
    }
}

// No Docker needed — config rules and SDK discovery.
public sealed class SshCommandConfigTests
{
    [Theory]
    [InlineData("", "root", "echo hi", "pw", null)]
    [InlineData("localhost", "", "echo hi", "pw", null)]
    [InlineData("localhost", "root", "", "pw", null)]
    [InlineData("localhost", "root", "echo hi", null, null)]
    public async Task Invalid_config_is_rejected_before_connecting(
        string host, string username, string command, string? password, string? privateKey)
    {
        var config = new SshCommandConfig(host, username, command, Port: 1, Password: password, PrivateKey: privateKey);

        var context = new ActionContext { Logger = NullLogger.Instance, Http = new HttpClient() };

        await Assert.ThrowsAsync<ArgumentException>(() => new SshCommandAction().ExecuteAsync(config, context));
    }

    [Fact]
    public void Ssh_action_is_discoverable_with_schema()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddHttpClient()
            .AddSingleton<ActionContextFactory>()
            .BuildServiceProvider();

        var actions = ActionDiscovery.FromAssembly(typeof(SshCommandAction).Assembly, "ssh", services).ToList();

        var action = Assert.Single(actions, x => x.Descriptor.Type == "ssh.command");
        Assert.NotNull(action.Descriptor.ConfigSchema);
        Assert.Contains("host", action.Descriptor.ConfigSchema.ToJsonString());
        Assert.Contains("privateKey", action.Descriptor.ConfigSchema.ToJsonString());
    }
}
