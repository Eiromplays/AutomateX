using AutomateX.Engine.Actions;
using AutomateX.Plugin.Sdk;
using AutomateX.Plugins.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutomateX.Tests;

// email.send delegates the actual SMTP to IEmailSender (MailKit in prod), so the action's
// own rules — config validation, server/message mapping, failure-to-step-failure — are
// unit-tested with a fake sender; no live SMTP server needed.
public sealed class EmailSendActionTests
{
    private sealed class FakeEmailSender : IEmailSender
    {
        public EmailServer? Server { get; private set; }

        public EmailMessage? Message { get; private set; }

        public Exception? ThrowOnSend { get; init; }

        public int Calls { get; private set; }

        public Task SendAsync(EmailServer server, EmailMessage message, CancellationToken cancellationToken)
        {
            Calls++;
            Server = server;
            Message = message;
            return ThrowOnSend is null ? Task.CompletedTask : throw ThrowOnSend;
        }
    }

    private static ActionContext Context() => new()
    {
        Logger = NullLogger.Instance,
        Http = new HttpClient(),
        ExecutionId = Guid.CreateVersion7(),
        WorkflowId = Guid.CreateVersion7(),
        StepOrder = 0,
    };

    private static EmailSendConfig Config(string body = "Body", bool isHtml = false) => new(
        Host: "smtp.example.com",
        Port: "587",
        Username: "user@example.com",
        Password: "secret",
        From: "bot@example.com",
        To: "me@example.com",
        Subject: "Subject",
        Body: body,
        IsHtml: isHtml);

    [Fact]
    public async Task Sends_message_to_the_configured_server()
    {
        var sender = new FakeEmailSender();

        var result = await new SendMessageAction(sender).ExecuteAsync(Config(), Context());

        Assert.Equal(1, sender.Calls);
        Assert.Equal("smtp.example.com", sender.Server!.Host);
        Assert.Equal(587, sender.Server.Port);
        Assert.True(sender.Server.UseStartTls);
        Assert.Equal("bot@example.com", sender.Message!.From);
        Assert.Equal("me@example.com", sender.Message.To);
        Assert.Equal("Subject", sender.Message.Subject);
        Assert.Equal("Body", sender.Message.Body);
        Assert.False(sender.Message.IsHtml);
        Assert.Equal("me@example.com", result.To);
    }

    [Fact]
    public async Task Html_flag_marks_the_body_html()
    {
        var sender = new FakeEmailSender();

        await new SendMessageAction(sender).ExecuteAsync(Config(isHtml: true), Context());

        Assert.True(sender.Message!.IsHtml);
    }

    [Fact]
    public async Task Send_failure_becomes_a_step_failure()
    {
        var sender = new FakeEmailSender { ThrowOnSend = new InvalidOperationException("smtp refused 535") };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SendMessageAction(sender).ExecuteAsync(Config(), Context()));

        Assert.Contains("email.send failed", exception.Message);
        Assert.Contains("535", exception.Message);
    }

    [Theory]
    [InlineData("", "587", "from@x", "to@x", "Subj", "Body")]
    [InlineData("smtp", "not-a-port", "from@x", "to@x", "Subj", "Body")]
    [InlineData("smtp", "70000", "from@x", "to@x", "Subj", "Body")]
    [InlineData("smtp", "587", "", "to@x", "Subj", "Body")]
    [InlineData("smtp", "587", "from@x", "", "Subj", "Body")]
    [InlineData("smtp", "587", "from@x", "to@x", "", "Body")]
    [InlineData("smtp", "587", "from@x", "to@x", "Subj", "")]
    public async Task Invalid_config_is_rejected_before_sending(
        string host, string port, string from, string to, string subject, string body)
    {
        var sender = new FakeEmailSender();
        var config = new EmailSendConfig(host, port, "user", "pw", from, to, subject, body);

        await Assert.ThrowsAsync<ArgumentException>(
            () => new SendMessageAction(sender).ExecuteAsync(config, Context()));

        Assert.Equal(0, sender.Calls);
    }

    [Fact]
    public void Email_action_is_discoverable_with_schema()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddHttpClient()
            .AddSingleton<ActionContextFactory>()
            .BuildServiceProvider();

        var actions = ActionDiscovery.FromAssembly(typeof(SendMessageAction).Assembly, "email", services).ToList();

        var action = Assert.Single(actions, x => x.Descriptor.Type == "email.send");
        Assert.NotNull(action.Descriptor.ConfigSchema);
        Assert.Contains("subject", action.Descriptor.ConfigSchema.ToJsonString());
    }
}
