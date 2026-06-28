using AutomateX.Engine.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace AutomateX.Tests;

// v4.0: proves the PluginHost's AssemblyLoadContext resolves a plugin's bundled NuGet dependencies.
// The Email plugin pulls in MailKit/MimeKit; the sample plugin has no third-party deps, so this is the
// only test that exercises transitive dependency loading out-of-process.
public sealed class OutOfProcDependencyTests(EngineFixture fixture, ITestOutputHelper output)
    : IClassFixture<EngineFixture>
{
    [Fact]
    public async Task Plugin_with_nuget_dependencies_loads_and_runs_out_of_process()
    {
        var (hostDll, emailDll) = Locate();
        if (!OutOfProcGate.Ready(hostDll, emailDll, output))
        {
            return;
        }

        var supervisor = new PluginProcessSupervisor(
            fixture.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            fixture.Host.Services.GetRequiredService<ILoggerFactory>(),
            hostDll!);

        await using (supervisor)
        {
            // Valid config (passes the action's validation) aimed at a closed port: email.send builds
            // MailKit's SmtpClient — forcing the dependency to load — then fails fast on connect.
            const string config = """
                {"host":"127.0.0.1","port":"1","username":"","password":"","from":"a@b.com","to":"c@d.com","subject":"hi","body":"x","useStartTls":false}
                """;

            var ex = await Record.ExceptionAsync(() => supervisor.ExecuteActionAsync(emailDll!, "email.send", config));

            Assert.NotNull(ex);
            // Had MailKit not resolved in the host ALC, this would be an assembly-load error instead.
            Assert.DoesNotContain("Could not load file or assembly", ex!.Message, StringComparison.OrdinalIgnoreCase);
            // Only thrown once SmtpClient.ConnectAsync runs — i.e. MailKit loaded and executed.
            Assert.Contains("email.send failed", ex.Message);
        }
    }

    private static (string? HostDll, string? EmailDll) Locate()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AutomateX.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            return (null, null);
        }

        var config = AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        const string tfm = "net10.0";

        var host = Path.Combine(dir.FullName, "src", "AutomateX.PluginHost", "bin", config, tfm, "AutomateX.PluginHost.dll");
        var email = Path.Combine(
            dir.FullName, "src", "Plugins", "AutomateX.Plugins.Email", "bin", config, tfm, "AutomateX.Plugins.Email.dll");
        return File.Exists(host) && File.Exists(email) ? (host, email) : (null, null);
    }
}
