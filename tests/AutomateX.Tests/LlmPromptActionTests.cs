using System.Net;
using System.Text;
using System.Text.Json;
using AutomateX.Engine.Actions;
using AutomateX.Plugin.Sdk;
using AutomateX.Plugins.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutomateX.Tests;

public sealed class LlmPromptActionTests
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

    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            """
            {
              "model": "gpt-4o-mini",
              "choices": [{"message": {"role": "assistant", "content": "Hello there."}}],
              "usage": {"prompt_tokens": 12, "completion_tokens": 4}
            }
            """,
            Encoding.UTF8,
            "application/json"),
    };

    private static ActionContext Context(FakeHandler handler) => new()
    {
        Logger = NullLogger.Instance,
        Http = new HttpClient(handler),
        ExecutionId = Guid.CreateVersion7(),
        WorkflowId = Guid.CreateVersion7(),
        StepOrder = 0,
    };

    private static LlmPromptConfig Config() => new(
        Model: "gpt-4o-mini",
        Prompt: "Say hello",
        ApiKey: "sk-secret");

    [Fact]
    public async Task Sends_an_openai_compatible_chat_completion()
    {
        var handler = new FakeHandler(_ => Ok());

        var result = await new LlmPromptAction().ExecuteAsync(Config(), Context(handler));

        var (request, body) = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.openai.com/v1/chat/completions", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("sk-secret", request.Headers.Authorization?.Parameter);

        using var json = JsonDocument.Parse(body);
        Assert.Equal("gpt-4o-mini", json.RootElement.GetProperty("model").GetString());
        var message = Assert.Single(json.RootElement.GetProperty("messages").EnumerateArray().ToList());
        Assert.Equal("user", message.GetProperty("role").GetString());
        Assert.Equal("Say hello", message.GetProperty("content").GetString());

        Assert.Equal("Hello there.", result.Text);
        Assert.Equal("gpt-4o-mini", result.Model);
        Assert.Equal(12, result.PromptTokens);
        Assert.Equal(4, result.CompletionTokens);
    }

    [Fact]
    public async Task System_message_prepends_when_configured()
    {
        var handler = new FakeHandler(_ => Ok());
        var config = Config() with { System = "You are terse." };

        await new LlmPromptAction().ExecuteAsync(config, Context(handler));

        using var json = JsonDocument.Parse(Assert.Single(handler.Calls).Body);
        var messages = json.RootElement.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(2, messages.Count);
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("You are terse.", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
    }

    [Fact]
    public async Task Api_key_is_optional_for_local_endpoints()
    {
        var handler = new FakeHandler(_ => Ok());
        var config = Config() with { ApiKey = null, BaseUrl = "http://localhost:11434/" };

        await new LlmPromptAction().ExecuteAsync(config, Context(handler));

        var (request, _) = Assert.Single(handler.Calls);
        Assert.Null(request.Headers.Authorization);
        Assert.Equal("http://localhost:11434/v1/chat/completions", request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Optional_sampling_params_are_omitted_unless_set()
    {
        var handler = new FakeHandler(_ => Ok());

        await new LlmPromptAction().ExecuteAsync(Config(), Context(handler));
        await new LlmPromptAction().ExecuteAsync(
            Config() with { Temperature = 0.2, MaxTokens = 100 }, Context(handler));

        using var bare = JsonDocument.Parse(handler.Calls[0].Body);
        Assert.False(bare.RootElement.TryGetProperty("temperature", out _));
        Assert.False(bare.RootElement.TryGetProperty("max_tokens", out _));

        using var tuned = JsonDocument.Parse(handler.Calls[1].Body);
        Assert.Equal(0.2, tuned.RootElement.GetProperty("temperature").GetDouble());
        Assert.Equal(100, tuned.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task Error_status_fails_with_details()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("""{"error":{"message":"rate limited"}}"""),
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new LlmPromptAction().ExecuteAsync(Config(), Context(handler)));

        Assert.Contains("429", exception.Message);
        Assert.Contains("rate limited", exception.Message);
    }

    [Theory]
    [InlineData("", "prompt")]
    [InlineData("model", "")]
    public async Task Missing_required_config_is_rejected_before_sending(string model, string prompt)
    {
        var handler = new FakeHandler(_ => Ok());
        var config = new LlmPromptConfig(model, prompt);

        await Assert.ThrowsAsync<ArgumentException>(
            () => new LlmPromptAction().ExecuteAsync(config, Context(handler)));

        Assert.Empty(handler.Calls);
    }

    [Fact]
    public void Llm_action_is_discoverable_with_schema()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddHttpClient()
            .AddSingleton<ActionContextFactory>()
            .BuildServiceProvider();

        var actions = ActionDiscovery.FromAssembly(typeof(LlmPromptAction).Assembly, "llm", services).ToList();

        var action = Assert.Single(actions, x => x.Descriptor.Type == "llm.prompt");
        Assert.NotNull(action.Descriptor.ConfigSchema);
        Assert.Contains("prompt", action.Descriptor.ConfigSchema.ToJsonString());
        Assert.Contains("baseUrl", action.Descriptor.ConfigSchema.ToJsonString());
    }
}
