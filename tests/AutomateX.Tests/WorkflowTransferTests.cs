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
        ("rss", """{"url":"https://example.com/feed.xml"}"""),
    ];

    [Fact]
    public void Export_carries_portable_triggers_only()
    {
        var document = WorkflowTransfer.Export("wf", null, Steps, Triggers);

        var triggers = (JsonArray)document["triggers"]!;
        List<string> types = [];
        foreach (var trigger in triggers)
        {
            types.Add((string)trigger!["type"]!);
        }

        Assert.Equal(2, triggers.Count); // cron + rss; webhook + workflow are not portable
        Assert.Contains("cron", types);
        Assert.Contains("rss", types);
        Assert.DoesNotContain("webhook", types);
        Assert.DoesNotContain("workflow", types);
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
        Assert.Equal(2, parsed.Triggers.Count); // cron + rss
        Assert.Equal("cron", parsed.Triggers[0].Type);
        Assert.Contains("*/5 * * * *", parsed.Triggers[0].ConfigJson);
        Assert.Equal("rss", parsed.Triggers[1].Type);
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
        Assert.Empty(parsed.Triggers);
        Assert.Empty(parsed.Edges);
        Assert.False(parsed.ContinueOnFailure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Round_trip_preserves_continue_on_failure(bool continueOnFailure)
    {
        var document = WorkflowTransfer.Export("wf", "desc", Steps, Triggers, edges: null, continueOnFailure);

        Assert.Equal(continueOnFailure, (bool)document["continueOnFailure"]!);
        Assert.Equal(continueOnFailure, WorkflowTransfer.Parse(document).ContinueOnFailure);
    }

    [Fact]
    public void Round_trip_preserves_edges()
    {
        List<EdgeDefinition> edges =
        [
            new(0, 1, "ok"),
            new(0, 1, null),
        ];

        var document = WorkflowTransfer.Export("wf", "desc", Steps, Triggers, edges);
        var parsed = WorkflowTransfer.Parse(document);

        Assert.Equal(2, parsed.Edges.Count);
        Assert.Equal(new EdgeDefinition(0, 1, "ok"), parsed.Edges[0]);
        Assert.Equal(new EdgeDefinition(0, 1, null), parsed.Edges[1]);
    }

    [Fact]
    public void Edge_pointing_outside_the_step_set_is_rejected()
    {
        var document = WorkflowTransfer.Export("wf", "desc", Steps, Triggers, [new EdgeDefinition(0, 5, "ok")]);

        var exception = Assert.Throws<InvalidOperationException>(() => WorkflowTransfer.Parse(document));
        Assert.Contains("doesn't exist", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
