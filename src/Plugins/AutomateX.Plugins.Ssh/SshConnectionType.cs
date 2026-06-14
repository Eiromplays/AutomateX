using AutomateX.Plugin.Sdk;

namespace AutomateX.Plugins.Ssh;

[ConnectionType("ssh", "SSH", Description = "Credentials for connecting to a host over SSH.")]
public sealed class SshConnectionType : IConnectionType
{
    public IReadOnlyList<ConnectionField> Fields { get; } =
    [
        new("privateKey", "Private key", Required: false,
            HelpText: "PEM/OpenSSH private key contents. Preferred over a password — one of the two is required."),
        new("password", "Password", Required: false,
            HelpText: "Alternative to a private key — one of the two is required."),
        new("privateKeyPassphrase", "Key passphrase", Required: false,
            HelpText: "Only if the private key is encrypted."),
    ];
}
