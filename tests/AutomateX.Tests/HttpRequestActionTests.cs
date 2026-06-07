using System.Net;
using System.Text;
using AutomateX.Engine.Actions;
using AutomateX.Plugin.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutomateX.Tests;

public sealed class HttpRequestActionTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request, body));
            cancellationToken.ThrowIfCancellationRequested();
            return await respond(request).WaitAsync(cancellationToken);
        }
    }

    private static HttpResponseMessage Ok(string body = "pong") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static ActionContext Context(FakeHandler handler) => new()
    {
        Logger = NullLogger.Instance,
        Http = new HttpClient(handler),
        ExecutionId = Guid.CreateVersion7(),
        WorkflowId = Guid.CreateVersion7(),
        StepOrder = 0,
    };

    [Fact]
    public async Task Sends_method_url_headers_and_json_body_by_default()
    {
        var handler = new FakeHandler(_ => Task.FromResult(Ok()));
        var config = new HttpRequestConfig(
            "POST",
            "https://api.example.com/things",
            Body: """{"a":1}""",
            Headers: new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer secret-token",
                ["X-Custom"] = "42",
            });

        var result = await new HttpRequestAction().ExecuteAsync(config, Context(handler));

        var (request, body) = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.example.com/things", request.RequestUri!.ToString());
        Assert.Equal("Bearer secret-token", request.Headers.Authorization?.ToString());
        Assert.Equal("42", Assert.Single(request.Headers.GetValues("X-Custom")));
        Assert.Equal("application/json", request.Content?.Headers.ContentType?.MediaType);
        Assert.Equal("""{"a":1}""", body);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("pong", result.Body);
    }

    [Fact]
    public async Task Explicit_content_type_overrides_the_json_default()
    {
        var handler = new FakeHandler(_ => Task.FromResult(Ok()));
        var config = new HttpRequestConfig(
            "POST", "https://api.example.com", Body: "a=1", ContentType: "application/x-www-form-urlencoded");

        await new HttpRequestAction().ExecuteAsync(config, Context(handler));

        var (request, _) = Assert.Single(handler.Calls);
        Assert.Equal("application/x-www-form-urlencoded", request.Content?.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Error_statuses_succeed_by_default_with_the_status_in_the_result()
    {
        var handler = new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"message":"Not Found"}"""),
        }));

        var result = await new HttpRequestAction().ExecuteAsync(
            new HttpRequestConfig("GET", "https://api.example.com/missing"), Context(handler));

        Assert.Equal(404, result.StatusCode);
        Assert.Contains("Not Found", result.Body);
    }

    [Fact]
    public async Task FailOnErrorStatus_throws_with_status_and_body()
    {
        var handler = new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("kaboom"),
        }));
        var config = new HttpRequestConfig("GET", "https://api.example.com", FailOnErrorStatus: true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new HttpRequestAction().ExecuteAsync(config, Context(handler)));

        Assert.Contains("500", exception.Message);
        Assert.Contains("kaboom", exception.Message);
    }

    [Fact]
    public async Task Response_headers_are_captured_with_lowercase_keys()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = Ok();
            response.Headers.Add("X-Request-Id", "abc-123");
            return Task.FromResult(response);
        });

        var result = await new HttpRequestAction().ExecuteAsync(
            new HttpRequestConfig("GET", "https://api.example.com"), Context(handler));

        Assert.Equal("abc-123", result.Headers["x-request-id"]);
        Assert.StartsWith("application/json", result.Headers["content-type"]);
    }

    [Fact]
    public async Task Timeout_cancels_the_request()
    {
        var handler = new FakeHandler(async _ =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            return Ok();
        });
        var config = new HttpRequestConfig("GET", "https://api.example.com", TimeoutSeconds: 1);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new HttpRequestAction().ExecuteAsync(config, Context(handler)));
    }
}
