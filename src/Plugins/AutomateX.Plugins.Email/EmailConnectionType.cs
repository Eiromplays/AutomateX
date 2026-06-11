using System.Globalization;
using AutomateX.Plugin.Sdk;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace AutomateX.Plugins.Email;

[ConnectionType("email", "Email (SMTP)", Description = "An SMTP account for sending email notifications.")]
public sealed class EmailConnectionType : IConnectionType, IConnectionTester
{
    public IReadOnlyList<ConnectionField> Fields { get; } =
    [
        new("host", "SMTP host", Secret: false, HelpText: "e.g. smtp.gmail.com"),
        new("port", "Port", Secret: false, HelpText: "587 for STARTTLS, 465 for implicit TLS."),
        new("username", "Username", Secret: false, HelpText: "Usually the full email address."),
        new("password", "Password", HelpText: "An app password if your provider requires one."),
        new("from", "From address", Secret: false, HelpText: "The sender address, e.g. bot@yourdomain.com."),
    ];

    // Connect + authenticate (no email sent). Exceptions surface as the test failure.
    public async Task<ConnectionTestResult> TestAsync(
        IReadOnlyDictionary<string, string> values, HttpClient http, CancellationToken cancellationToken)
    {
        if (!values.TryGetValue("host", out var host) || string.IsNullOrWhiteSpace(host))
        {
            return new ConnectionTestResult(false, "No SMTP host set.");
        }

        if (!int.TryParse(values.GetValueOrDefault("port"), CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            return new ConnectionTestResult(false, "Invalid or missing port.");
        }

        using var client = new SmtpClient();
        var security = port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await client.ConnectAsync(host, port, security, cancellationToken);

        if (values.TryGetValue("username", out var username) && !string.IsNullOrEmpty(username))
        {
            await client.AuthenticateAsync(username, values.GetValueOrDefault("password") ?? string.Empty, cancellationToken);
        }

        await client.DisconnectAsync(quit: true, cancellationToken);
        return new ConnectionTestResult(true, "Connected and authenticated.");
    }
}
