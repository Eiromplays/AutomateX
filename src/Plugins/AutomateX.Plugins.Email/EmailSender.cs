using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace AutomateX.Plugins.Email;

public sealed record EmailServer(string Host, int Port, string Username, string Password, bool UseStartTls);

public sealed record EmailMessage(string From, string To, string Subject, string Body, bool IsHtml);

// The SMTP boundary — seamed so the action's logic is testable without a live server.
public interface IEmailSender
{
    Task SendAsync(EmailServer server, EmailMessage message, CancellationToken cancellationToken);
}

public sealed class SmtpEmailSender : IEmailSender
{
    public async Task SendAsync(EmailServer server, EmailMessage message, CancellationToken cancellationToken)
    {
        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(message.From));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new TextPart(message.IsHtml ? "html" : "plain") { Text = message.Body };

        using var client = new SmtpClient();
        var security = server.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect;
        await client.ConnectAsync(server.Host, server.Port, security, cancellationToken);

        if (!string.IsNullOrEmpty(server.Username))
        {
            await client.AuthenticateAsync(server.Username, server.Password, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
