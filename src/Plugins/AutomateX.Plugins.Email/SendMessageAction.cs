using System.Globalization;
using AutomateX.Plugin.Sdk;

namespace AutomateX.Plugins.Email;

// Host/port/username/password/from come from an 'email' connection
// (use {{connections.<name>.<field>}}); to/subject/body are the message. Port is a
// string so it templates cleanly from a connection value.
public sealed record EmailSendConfig(
    string Host,
    string Port,
    string Username,
    string Password,
    string From,
    string To,
    string Subject,
    string Body,
    bool IsHtml = false,
    bool UseStartTls = true);

public sealed record EmailSendResult(string To, string Subject);

[Action("email.send", "Email: Send",
    Description = "Sends an email over SMTP. Point host/port/username/password/from at an 'email' "
        + "connection; set to/subject/body. UseStartTls for port 587, off for implicit TLS (465).")]
public sealed class SendMessageAction(IEmailSender? sender = null) : IAction<EmailSendConfig, EmailSendResult>
{
    private readonly IEmailSender _sender = sender ?? new SmtpEmailSender();

    public async Task<EmailSendResult> ExecuteAsync(
        EmailSendConfig config,
        ActionContext context,
        CancellationToken cancellationToken = default)
    {
        var port = Validate(config);

        var server = new EmailServer(config.Host, port, config.Username, config.Password, config.UseStartTls);
        var message = new EmailMessage(config.From, config.To, config.Subject, config.Body, config.IsHtml);

        try
        {
            await _sender.SendAsync(server, message, cancellationToken);
        }
        catch (Exception ex) when (ex is not ArgumentException and not OperationCanceledException)
        {
            throw new InvalidOperationException($"email.send failed: {ex.Message}", ex);
        }

        return new EmailSendResult(config.To, config.Subject);
    }

    private static int Validate(EmailSendConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Host))
        {
            throw new ArgumentException("email.send requires 'host'.");
        }

        if (!int.TryParse(config.Port, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            throw new ArgumentException("email.send requires a valid 'port' (1-65535).");
        }

        if (string.IsNullOrWhiteSpace(config.From))
        {
            throw new ArgumentException("email.send requires 'from'.");
        }

        if (string.IsNullOrWhiteSpace(config.To))
        {
            throw new ArgumentException("email.send requires 'to'.");
        }

        if (string.IsNullOrWhiteSpace(config.Subject))
        {
            throw new ArgumentException("email.send requires 'subject'.");
        }

        if (string.IsNullOrWhiteSpace(config.Body))
        {
            throw new ArgumentException("email.send requires 'body'.");
        }

        return port;
    }
}
