using System.Text.Json;
using AutomateX.Engine.Templating;
using Xunit;

namespace AutomateX.Tests;

// Preview mode (UnresolvedSink set) collects every miss and substitutes a placeholder instead of
// throwing on the first — strict mode (sink null) is unchanged.
public sealed class TemplateResolverPreviewTests
{
    private static JsonElement El(object value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public void Strict_mode_still_throws_on_an_unresolved_path()
    {
        var context = new TemplateContext(null, new Dictionary<int, JsonElement>(), Guid.Empty, Guid.Empty);

        Assert.Throws<TemplateResolutionException>(() =>
            TemplateResolver.Resolve("""{"x":"{{trigger.payload.missing}}"}""", context));
    }

    [Fact]
    public void Preview_mode_collects_unresolved_and_substitutes_placeholders()
    {
        List<string> unresolved = [];
        var context = new TemplateContext(
            TriggerPayload: El(new { name = "Eirik" }),
            StepOutputs: new Dictionary<int, JsonElement>(),
            ExecutionId: Guid.Empty,
            WorkflowId: Guid.Empty,
            UnresolvedSink: unresolved);

        var result = TemplateResolver.Resolve(
            """{"hi":"{{trigger.payload.name}}","miss":"{{steps.2.output.id}}"}""", context);

        Assert.Contains("Eirik", result);
        Assert.Contains("[unresolved: steps.2.output.id]", result);
        Assert.Equal(["steps.2.output.id"], unresolved);
    }

    [Fact]
    public void Preview_mode_does_not_report_resolved_refs()
    {
        List<string> unresolved = [];
        var context = new TemplateContext(
            El(new { a = 1 }), new Dictionary<int, JsonElement>(), Guid.Empty, Guid.Empty, UnresolvedSink: unresolved);

        _ = TemplateResolver.Resolve("""{"x":"{{trigger.payload.a}}"}""", context);

        Assert.Empty(unresolved);
    }
}
