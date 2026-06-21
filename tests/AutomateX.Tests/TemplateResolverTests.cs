using System.Text.Json;
using AutomateX.Engine.Templating;
using Xunit;

namespace AutomateX.Tests;

// Rules encoded ahead of the implementation:
// - a {{token}} that is the ENTIRE string keeps the resolved JSON type
// - tokens inside larger strings interpolate (objects serialize compact)
// - roots: trigger.payload, steps.<order>.output, execution.id, workflow.id
// - navigation: object properties + array indices, case-sensitive (outputs are camelCase)
// - unresolvable anything throws TemplateResolutionException — fail fast, no retries
public sealed class TemplateResolverTests
{
    private static readonly Guid ExecutionId = Guid.Parse("00000000-0000-0000-0000-0000000000e1");
    private static readonly Guid WorkflowId = Guid.Parse("00000000-0000-0000-0000-0000000000f1");

    private static TemplateContext Context(string? payload = null, params (int Order, string Output)[] outputs) =>
        new(
            payload is null ? null : JsonSerializer.Deserialize<JsonElement>(payload),
            outputs.ToDictionary(x => x.Order, x => ParseLikeTheEngine(x.Output)),
            ExecutionId,
            WorkflowId);

    private static TemplateContext ContextWithConnections(params (string Name, string SecretsJson)[] connections) =>
        Context() with
        {
            Connections = connections.ToDictionary(
                x => x.Name,
                x => JsonSerializer.Deserialize<JsonElement>(x.SecretsJson)),
        };

    // Mirrors the engine: valid JSON outputs navigate; anything else is a string value.
    private static JsonElement ParseLikeTheEngine(string output)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(output);
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(output);
        }
    }

    private static JsonElement Resolve(string configJson, TemplateContext context) =>
        JsonSerializer.Deserialize<JsonElement>(TemplateResolver.Resolve(configJson, context));

    [Fact]
    public void Whole_string_tokens_preserve_json_types()
    {
        var result = Resolve(
            """{"n":"{{trigger.payload.count}}","o":"{{trigger.payload.obj}}","b":"{{trigger.payload.flag}}"}""",
            Context("""{"count":42,"obj":{"a":1},"flag":true}"""));

        Assert.Equal(42, result.GetProperty("n").GetInt32());
        Assert.Equal(1, result.GetProperty("o").GetProperty("a").GetInt32());
        Assert.True(result.GetProperty("b").GetBoolean());
    }

    [Fact]
    public void Tokens_inside_strings_interpolate()
    {
        var result = Resolve(
            """{"msg":"count is {{trigger.payload.count}}!"}""",
            Context("""{"count":42}"""));

        Assert.Equal("count is 42!", result.GetProperty("msg").GetString());
    }

    [Fact]
    public void Step_outputs_navigate_like_json()
    {
        var result = Resolve(
            """{"status":"{{steps.0.output.statusCode}}","body":"{{steps.0.output.body}}"}""",
            Context(outputs: (0, """{"statusCode":200,"body":"hi"}""")));

        Assert.Equal(200, result.GetProperty("status").GetInt32());
        Assert.Equal("hi", result.GetProperty("body").GetString());
    }

    [Fact]
    public void Non_json_step_output_resolves_as_string()
    {
        var result = Resolve(
            """{"x":"{{steps.0.output}}"}""",
            Context(outputs: (0, "ok:1")));

        Assert.Equal("ok:1", result.GetProperty("x").GetString());
    }

    [Fact]
    public void Array_indices_navigate()
    {
        var result = Resolve(
            """{"id":"{{trigger.payload.items.0.id}}"}""",
            Context("""{"items":[{"id":7}]}"""));

        Assert.Equal(7, result.GetProperty("id").GetInt32());
    }

    [Fact]
    public void Execution_and_workflow_ids_resolve()
    {
        var result = Resolve(
            """{"e":"{{execution.id}}","w":"{{workflow.id}}"}""",
            Context());

        Assert.Equal(ExecutionId.ToString(), result.GetProperty("e").GetString());
        Assert.Equal(WorkflowId.ToString(), result.GetProperty("w").GetString());
    }

    [Fact]
    public void Config_without_tokens_is_equivalent()
    {
        var result = Resolve("""{"a":1,"b":[true,null],"c":{"d":"plain"}}""", Context());

        Assert.Equal(1, result.GetProperty("a").GetInt32());
        Assert.Equal("plain", result.GetProperty("c").GetProperty("d").GetString());
    }

    [Fact]
    public void Missing_step_output_throws()
    {
        var ex = Assert.Throws<TemplateResolutionException>(() =>
            TemplateResolver.Resolve("""{"x":"{{steps.3.output}}"}""", Context()));

        Assert.Contains("step '3'", ex.Message);
    }

    [Fact]
    public void Step_output_resolves_by_key()
    {
        var context = Context(outputs: (2, """{"stdout":"deployed"}""")) with
        {
            StepKeys = new Dictionary<string, int> { ["ssh-deploy"] = 2 },
        };

        var result = Resolve("""{"x":"{{steps.ssh-deploy.output.stdout}}"}""", context);

        Assert.Equal("deployed", result.GetProperty("x").GetString());
    }

    [Fact]
    public void Numeric_and_key_refs_address_the_same_step()
    {
        var context = Context(outputs: (2, """{"stdout":"deployed"}""")) with
        {
            StepKeys = new Dictionary<string, int> { ["ssh-deploy"] = 2 },
        };

        Assert.Equal(
            Resolve("""{"x":"{{steps.2.output.stdout}}"}""", context).GetProperty("x").GetString(),
            Resolve("""{"x":"{{steps.ssh-deploy.output.stdout}}"}""", context).GetProperty("x").GetString());
    }

    [Fact]
    public void Step_error_resolves_on_the_error_lane()
    {
        var context = Context() with
        {
            StepKeys = new Dictionary<string, int> { ["deploy"] = 1 },
            StepErrors = new Dictionary<int, JsonElement>
            {
                [1] = JsonSerializer.Deserialize<JsonElement>("""{"message":"boom"}"""),
            },
        };

        var result = Resolve("""{"x":"Deploy failed: {{steps.deploy.error.message}}"}""", context);

        Assert.Equal("Deploy failed: boom", result.GetProperty("x").GetString());
    }

    [Fact]
    public void Step_error_throws_when_the_step_did_not_fail()
    {
        var context = Context(outputs: (0, "{}")) with
        {
            StepKeys = new Dictionary<string, int> { ["probe"] = 0 },
        };

        var ex = Assert.Throws<TemplateResolutionException>(() =>
            TemplateResolver.Resolve("""{"x":"{{steps.probe.error.message}}"}""", context));

        Assert.Contains("no error", ex.Message);
    }

    [Fact]
    public void Unknown_step_key_throws()
    {
        var context = Context(outputs: (0, "{}")) with
        {
            StepKeys = new Dictionary<string, int> { ["probe"] = 0 },
        };

        var ex = Assert.Throws<TemplateResolutionException>(() =>
            TemplateResolver.Resolve("""{"x":"{{steps.nope.output}}"}""", context));

        Assert.Contains("unknown step 'nope'", ex.Message);
    }

    [Fact]
    public void Missing_payload_throws()
    {
        Assert.Throws<TemplateResolutionException>(() =>
            TemplateResolver.Resolve("""{"x":"{{trigger.payload.name}}"}""", Context(payload: null)));
    }

    [Fact]
    public void Unknown_root_throws()
    {
        var ex = Assert.Throws<TemplateResolutionException>(() =>
            TemplateResolver.Resolve("""{"x":"{{nope.thing}}"}""", Context()));

        Assert.Contains("unknown root", ex.Message);
    }

    [Fact]
    public void Connection_fields_resolve()
    {
        var result = Resolve(
            """{"auth":"Bearer {{connections.github.token}}"}""",
            ContextWithConnections(("github", """{"token":"s3cret"}""")));

        Assert.Equal("Bearer s3cret", result.GetProperty("auth").GetString());
    }

    [Fact]
    public void Unknown_connection_throws_without_echoing_values()
    {
        var ex = Assert.Throws<TemplateResolutionException>(() =>
            TemplateResolver.Resolve(
                """{"x":"{{connections.missing.token}}"}""",
                ContextWithConnections(("github", """{"token":"s3cret"}"""))));

        Assert.Contains("missing", ex.Message);
        Assert.DoesNotContain("s3cret", ex.Message);
    }

    [Fact]
    public void Connection_values_are_collected_for_masking()
    {
        var sink = new HashSet<string>();
        var context = ContextWithConnections(("github", """{"token":"s3cret"}""")) with { SecretSink = sink };

        TemplateResolver.Resolve(
            """{"auth":"Bearer {{connections.github.token}}","id":"{{execution.id}}"}""",
            context);

        // Only connection-resolved values are secrets — execution metadata is not.
        Assert.Equal(["s3cret"], sink);
    }

    [Fact]
    public void Missing_connection_field_throws()
    {
        Assert.Throws<TemplateResolutionException>(() =>
            TemplateResolver.Resolve(
                """{"x":"{{connections.github.nope}}"}""",
                ContextWithConnections(("github", """{"token":"s3cret"}"""))));
    }

    [Fact]
    public void Missing_property_segment_throws()
    {
        Assert.Throws<TemplateResolutionException>(() =>
            TemplateResolver.Resolve(
                """{"x":"{{trigger.payload.missing.deep}}"}""",
                Context("""{"present":1}""")));
    }
}
