using System.Text;
using AutomateX.Plugin.Sdk;
using Microsoft.Extensions.Logging;
using Renci.SshNet;

namespace AutomateX.Plugins.Ssh;

public sealed record SshCommandConfig(
    string Host,
    string Username,
    [property: Multiline] string Command,
    int Port = 22,
    string? Password = null,
    string? PrivateKey = null,
    string? PrivateKeyPassphrase = null,
    string? HostFingerprint = null,
    int TimeoutSeconds = 60);

public sealed record SshCommandResult(int ExitCode, string Stdout, string Stderr);

[Action("ssh.command", "SSH: Run Command",
    Description = "Runs a command on a remote host over SSH. Auth via password or private key content "
        + "(use {{connections.<name>.privateKey}}). Optional hostFingerprint (SHA256:… from ssh-keygen -lf) "
        + "pins the server's host key. A non-zero exit code fails the step.")]
public sealed class SshCommandAction : IAction<SshCommandConfig, SshCommandResult>
{
    public async Task<SshCommandResult> ExecuteAsync(
        SshCommandConfig config,
        ActionContext context,
        CancellationToken cancellationToken = default)
    {
        Validate(config);

        var timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
        using var client = CreateClient(config);
        client.ConnectionInfo.Timeout = timeout;

        if (config.HostFingerprint is { Length: > 0 } expected)
        {
            client.HostKeyReceived += (_, e) =>
                e.CanTrust = string.Equals(Normalize(e.FingerPrintSHA256), Normalize(expected), StringComparison.Ordinal);
        }

        // One budget for the whole operation: connect + command.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        await client.ConnectAsync(timeoutCts.Token);

        using var command = client.CreateCommand(config.Command);
        await command.ExecuteAsync(timeoutCts.Token);

        var exitCode = command.ExitStatus ?? -1;
        context.Logger.LogInformation(
            "ssh.command on {Host}:{Port} exited with {ExitCode}", config.Host, config.Port, exitCode);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"ssh.command exited with code {exitCode}: {Truncate(command.Error.Length > 0 ? command.Error : command.Result)}");
        }

        return new SshCommandResult(exitCode, command.Result, command.Error);
    }

    private static SshClient CreateClient(SshCommandConfig config)
    {
        if (config.PrivateKey is { Length: > 0 })
        {
            var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(config.PrivateKey));
            var keyFile = config.PrivateKeyPassphrase is { Length: > 0 }
                ? new PrivateKeyFile(keyStream, config.PrivateKeyPassphrase)
                : new PrivateKeyFile(keyStream);
            return new SshClient(config.Host, config.Port, config.Username, keyFile);
        }

        // Validate() guarantees an auth method; no PrivateKey means Password is present.
        return new SshClient(config.Host, config.Port, config.Username, config.Password!);
    }

    private static void Validate(SshCommandConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Host))
        {
            throw new ArgumentException("ssh.command requires 'host'.");
        }

        if (string.IsNullOrWhiteSpace(config.Username))
        {
            throw new ArgumentException("ssh.command requires 'username'.");
        }

        if (string.IsNullOrWhiteSpace(config.Command))
        {
            throw new ArgumentException("ssh.command requires 'command'.");
        }

        if (string.IsNullOrWhiteSpace(config.Password) && string.IsNullOrWhiteSpace(config.PrivateKey))
        {
            throw new ArgumentException("ssh.command requires either 'password' or 'privateKey'.");
        }
    }

    // Accepts both ssh-keygen output ("SHA256:<base64>") and bare base64, with or without padding.
    private static string Normalize(string fingerprint)
    {
        var value = fingerprint.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase)
            ? fingerprint[7..]
            : fingerprint;
        return value.TrimEnd('=').Trim();
    }

    private static string Truncate(string value) =>
        value.Length <= 2000 ? value.Trim() : value[..2000].Trim() + "…";
}
