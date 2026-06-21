using System.Text.Json;
using AutomateX.Engine.Actions;
using AutomateX.Plugin.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutomateX.Tests;

// transform: run a JMESPath query over the (resolved) input JSON; the result is the step output
// directly. Invalid/empty queries are deterministic authoring errors (ArgumentException).
public sealed class TransformActionTests
{
    private static ActionContext Context() => new()
    {
        Logger = NullLogger.Instance,
        Http = new HttpClient(),
        ExecutionId = Guid.CreateVersion7(),
        WorkflowId = Guid.CreateVersion7(),
        StepOrder = 0,
    };

    private static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static Task<JsonElement> Run(string input, string query) =>
        new TransformAction().ExecuteAsync(new TransformConfig(Json(input), query), Context());

    [Fact]
    public async Task Filters_and_projects()
    {
        var result = await Run("""{"items":[{"id":1,"ok":true},{"id":2,"ok":false}]}""", "items[?ok].id");

        Assert.Equal(JsonValueKind.Array, result.ValueKind);
        Assert.Equal(1, result.GetArrayLength());
        Assert.Equal(1, result[0].GetInt32());
    }

    [Fact]
    public async Task Reshapes_into_a_new_object()
    {
        var result = await Run(
            """{"items":[{"id":1},{"id":2}]}""",
            "{count: length(items), ids: items[].id}");

        Assert.Equal(2, result.GetProperty("count").GetInt32());
        Assert.Equal([1, 2], result.GetProperty("ids").EnumerateArray().Select(x => x.GetInt32()));
    }

    [Fact]
    public async Task Extracts_a_nested_scalar()
    {
        var result = await Run("""{"release":{"tag_name":"v3.1"}}""", "release.tag_name");

        Assert.Equal("v3.1", result.GetString());
    }

    [Fact]
    public async Task No_match_yields_null()
    {
        var result = await Run("""{"items":[]}""", "items[0].id");

        Assert.Equal(JsonValueKind.Null, result.ValueKind);
    }

    [Theory]
    [InlineData("items[")]
    [InlineData("{bad")]
    public async Task Invalid_query_is_rejected(string query)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Run("""{"items":[]}""", query));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_query_is_rejected(string query)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Run("{}", query));
    }

    [Fact]
    public void Transform_is_discoverable_as_a_builtin_with_schema()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddHttpClient()
            .AddSingleton<ActionContextFactory>()
            .BuildServiceProvider();

        var actions = ActionDiscovery.FromAssembly(typeof(TransformAction).Assembly, "builtin", services).ToList();

        var action = Assert.Single(actions, x => x.Descriptor.Type == "transform");
        Assert.NotNull(action.Descriptor.ConfigSchema);
        Assert.Contains("query", action.Descriptor.ConfigSchema.ToJsonString());
    }
}
