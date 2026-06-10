using AutomateX.Plugin.Sdk;

namespace AutomateX.Plugins.Email;

[ConnectionType("email", "Email (SMTP)", Description = "An SMTP account for sending email notifications.")]
public sealed class EmailConnectionType : IConnectionType
{
    public IReadOnlyList<ConnectionField> Fields { get; } =
    [
        new("host", "SMTP host", Secret: false, HelpText: "e.g. smtp.gmail.com"),
        new("port", "Port", Secret: false, HelpText: "587 for STARTTLS, 465 for implicit TLS."),
        new("username", "Username", Secret: false, HelpText: "Usually the full email address."),
        new("password", "Password", HelpText: "An app password if your provider requires one."),
        new("from", "From address", Secret: false, HelpText: "The sender address, e.g. bot@yourdomain.com."),
    ];
}
