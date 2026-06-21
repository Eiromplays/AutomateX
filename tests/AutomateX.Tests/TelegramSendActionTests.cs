using System.Net;
using System.Text;
using AutomateX.Engine.Actions;
using AutomateX.Plugin.Sdk;
using AutomateX.Plugins.Telegram;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutomateX.Tests;

// telegram.send: POST chat_id + text to https://api.telegram.org/bot<token>/sendMessage.
// Success returns {"ok":true,"result":{"message_id":N,…}}; non-2xx fails the step.
public sealed class TelegramSendActionTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request, body));
            return respond(request);
        }
    }

    private static HttpResponseMessage Ok(long messageId = 42) => new(HttpStatusCode.OK)
    {
        Content = new StringContent($$$"""{"ok":true,"result":{"message_id":{{{messageId}}}}}""", Encoding.UTF8, "application/json"),
    };

    private static ActionContext Context(FakeHandler handler) => new()
    {
        Logger = NullLogger.Instance,
        Http = new HttpClient(handler),
        ExecutionId = Guid.CreateVersion7(),
        WorkflowId = Guid.CreateVersion7(),
        StepOrder = 0,
    };

    private static TelegramSendConfig Config(string text = "hello") => new(
        BotToken: "123:ABC",
        ChatId: "987654",
        Text: text);

    [Fact]
    public async Task Posts_message_to_the_bot_api()
    {
        var handler = new FakeHandler(_ => Ok(messageId: 7));

        var result = await new SendMessageAction().ExecuteAsync(Config(), Context(handler));

        var (request, body) = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.telegram.org/bot123:ABC/sendMessage", request.RequestUri!.ToString());
        Assert.Contains("chat_id=987654", body);
        Assert.Contains("text=hello", body);
        Assert.Equal(7, result.MessageId);
    }

    [Fact]
    public async Task Parse_mode_is_sent_when_set()
    {
        var handler = new FakeHandler(_ => Ok());

        await new SendMessageAction().ExecuteAsync(Config() with { ParseMode = "HTML" }, Context(handler));

        var (_, body) = Assert.Single(handler.Calls);
        Assert.Contains("parse_mode=HTML", body);
    }

    [Fact]
    public async Task Parse_mode_is_omitted_when_unset()
    {
        var handler = new FakeHandler(_ => Ok());

        await new SendMessageAction().ExecuteAsync(Config(), Context(handler));

        var (_, body) = Assert.Single(handler.Calls);
        Assert.DoesNotContain("parse_mode", body);
    }

    [Fact]
    public async Task Failure_status_throws_with_details()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"ok":false,"description":"chat not found"}""", Encoding.UTF8, "application/json"),
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SendMessageAction().ExecuteAsync(Config(), Context(handler)));

        Assert.Contains("400", exception.Message);
        Assert.Contains("chat not found", exception.Message);
    }

    [Theory]
    [InlineData("", "1", "hi")]
    [InlineData("123:ABC", "", "hi")]
    [InlineData("123:ABC", "1", "")]
    public async Task Invalid_config_is_rejected_before_sending(string token, string chatId, string text)
    {
        var handler = new FakeHandler(_ => Ok());

        await Assert.ThrowsAsync<ArgumentException>(
            () => new SendMessageAction().ExecuteAsync(new TelegramSendConfig(token, chatId, text), Context(handler)));

        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task Invalid_parse_mode_is_rejected()
    {
        var handler = new FakeHandler(_ => Ok());

        await Assert.ThrowsAsync<ArgumentException>(
            () => new SendMessageAction().ExecuteAsync(Config() with { ParseMode = "rtf" }, Context(handler)));

        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task Connection_test_succeeds_when_get_me_ok()
    {
        var handler = new FakeHandler(r =>
        {
            Assert.Equal("https://api.telegram.org/bot123:ABC/getMe", r.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var result = await new TelegramConnectionType().TestAsync(
            new Dictionary<string, string> { ["botToken"] = "123:ABC" }, new HttpClient(handler), CancellationToken.None);

        Assert.True(result.Ok);
    }

    [Fact]
    public async Task Connection_test_fails_on_error_status()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var result = await new TelegramConnectionType().TestAsync(
            new Dictionary<string, string> { ["botToken"] = "bad" }, new HttpClient(handler), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains("401", result.Message);
    }

    [Fact]
    public void Telegram_action_is_discoverable_with_schema()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddHttpClient()
            .AddSingleton<ActionContextFactory>()
            .BuildServiceProvider();

        var actions = ActionDiscovery.FromAssembly(typeof(SendMessageAction).Assembly, "telegram", services).ToList();

        var action = Assert.Single(actions, x => x.Descriptor.Type == "telegram.send");
        Assert.NotNull(action.Descriptor.ConfigSchema);
        Assert.Contains("chatId", action.Descriptor.ConfigSchema.ToJsonString());
    }
}
