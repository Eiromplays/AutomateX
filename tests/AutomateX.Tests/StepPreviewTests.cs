using System.Text.Json;
using AutomateX.Engine.Templating;
using Xunit;

namespace AutomateX.Tests;

public sealed class StepPreviewTests
{
    private static JsonElement El(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    [Fact]
    public void Resolves_against_sample_context_and_lists_unresolved()
    {
        var result = StepPreview.Build(
            configJson: """{"to":"{{trigger.payload.email}}","ref":"{{steps.first.output.id}}","miss":"{{steps.9.output.x}}"}""",
            triggerPayload: El("""{"email":"a@b.com"}"""),
            stepOutputs: new Dictionary<int, JsonElement> { [0] = El("""{"id":"abc"}""") },
            stepKeys: new Dictionary<string, int> { ["first"] = 0 },
            connectionFields: new Dictionary<string, IReadOnlyList<string>>(),
            variables: new Dictionary<string, string>(),
            workflowId: Guid.NewGuid());

        Assert.Contains("a@b.com", result.ResolvedConfig);
        Assert.Contains("abc", result.ResolvedConfig);
        Assert.Equal(["steps.9.output.x"], result.Unresolved);
    }

    [Fact]
    public void Masks_connection_values_and_reports_fields_used()
    {
        var result = StepPreview.Build(
            configJson: """{"host":"{{connections.smtp.host}}","pass":"{{connections.smtp.password}}"}""",
            triggerPayload: null,
            stepOutputs: new Dictionary<int, JsonElement>(),
            stepKeys: new Dictionary<string, int>(),
            connectionFields: new Dictionary<string, IReadOnlyList<string>> { ["smtp"] = ["host", "password"] },
            variables: new Dictionary<string, string>(),
            workflowId: Guid.NewGuid());

        Assert.DoesNotContain("[unresolved", result.ResolvedConfig);
        Assert.Contains("******", result.ResolvedConfig);

        var usage = Assert.Single(result.ConnectionsUsed);
        Assert.Equal("smtp", usage.Name);
        Assert.Equal(["host", "password"], usage.Fields);
    }

    [Fact]
    public void Unknown_connection_ref_is_unresolved()
    {
        var result = StepPreview.Build(
            configJson: """{"x":"{{connections.ghost.token}}"}""",
            triggerPayload: null,
            stepOutputs: new Dictionary<int, JsonElement>(),
            stepKeys: new Dictionary<string, int>(),
            connectionFields: new Dictionary<string, IReadOnlyList<string>>(),
            variables: new Dictionary<string, string>(),
            workflowId: Guid.NewGuid());

        Assert.Equal(["connections.ghost.token"], result.Unresolved);
    }
}
