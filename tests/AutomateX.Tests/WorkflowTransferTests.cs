using System.Text.Json.Nodes;
using AutomateX.Modules.Workflows;
using Xunit;

namespace AutomateX.Tests;

// The transfer document: secrets can't travel by construction — webhook triggers
// (per-trigger secrets) and chain triggers (instance-local ids) are excluded;
// connections ride as name references only (inside step configs).
public sealed class WorkflowTransferTests
{
    private static readonly List<StepDefinition> Steps =
    [
        new("http.request", "fetch", """{"method":"GET","url":"https://example.com"}"""),
        new("matrix.send", null, """{"accessToken":"{{connections.matrix.accessToken}}","message":"hi"}"""),
    ];

    private static readonly List<(string Type, string ConfigJson)> Triggers =
    [
        ("cron", """{"cron":"*/5 * * * *"}"""),
        ("webhook", """{"secretHash":"super-secret"}"""),
        ("workflow", """{"workflowId":"0199c986-0000-7000-8000-000000000000","on":"succeeded"}"""),
    ];

    [Fact]
    public void Export_carries_only_cron_triggers()
    {
        var document = WorkflowTransfer.Export("wf", null, Steps, Triggers);

        var triggers = (JsonArray)document["triggers"]!;
        var trigger = Assert.Single(triggers);
        Assert.Equal("cron", (string)trigger!["type"]!);
        Assert.DoesNotContain("secret", document.ToJsonString());
    }

    [Fact]
    public void Export_inlines_step_configs_as_json_objects()
    {
        var document = WorkflowTransfer.Export("wf", "desc", Steps, Triggers);

        Assert.Equal(1, (int)document["automatex"]!);
        Assert.Equal("wf", (string)document["name"]!);
        var steps = (JsonArray)document["steps"]!;
        Assert.Equal(2, steps.Count);
        Assert.Equal("GET", (string)steps[0]!["config"]!["method"]!);
        Assert.Equal("{{connections.matrix.accessToken}}", (string)steps[1]!["config"]!["accessToken"]!);
    }

    [Fact]
    public void Round_trip_preserves_steps_and_cron_triggers()
    {
        var document = WorkflowTransfer.Export("wf", "desc", Steps, Triggers);

        var parsed = WorkflowTransfer.Parse(document);

        Assert.Equal("wf", parsed.Name);
        Assert.Equal("desc", parsed.Description);
        Assert.Equal(2, parsed.Steps.Count);
        Assert.Equal("http.request", parsed.Steps[0].ActionType);
        Assert.Equal("fetch", parsed.Steps[0].Name);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse(Steps[1].ConfigJson),
            JsonNode.Parse(parsed.Steps[1].ConfigJson)));
        var cron = Assert.Single(parsed.CronTriggerConfigs);
        Assert.Contains("*/5 * * * *", cron);
    }

    [Theory]
    [InlineData("""{"name":"wf","steps":[]}""")]
    [InlineData("""{"automatex":2,"name":"wf","steps":[]}""")]
    public void Unsupported_or_missing_format_version_is_rejected(string json)
    {
        var document = (JsonObject)JsonNode.Parse(json)!;

        var exception = Assert.Throws<InvalidOperationException>(() => WorkflowTransfer.Parse(document));
        Assert.Contains("format", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_name_is_rejected()
    {
        var document = (JsonObject)JsonNode.Parse("""{"automatex":1,"steps":[]}""")!;

        Assert.Throws<InvalidOperationException>(() => WorkflowTransfer.Parse(document));
    }

    [Fact]
    public void Optionals_default_cleanly()
    {
        var document = (JsonObject)JsonNode.Parse(
            """{"automatex":1,"name":"bare","steps":[{"actionType":"http.request"}]}""")!;

        var parsed = WorkflowTransfer.Parse(document);

        Assert.Null(parsed.Description);
        var step = Assert.Single(parsed.Steps);
        Assert.Null(step.Name);
        Assert.Equal("{}", step.ConfigJson);
        Assert.Empty(parsed.CronTriggerConfigs);
    }
}
