using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using AutomateX.Modules.Workflows;
using Xunit;

namespace AutomateX.Tests;

// Guards the importable documents under /examples against format drift: each must still parse with
// the live importer, and the watchdog's branch / fan-out / join shape is pinned exactly so a stray
// edit to the example can't silently break it.
public sealed class ExampleWorkflowTests
{
    [Theory]
    [InlineData("api-uptime-watchdog.automatex.json")]
    public void Parses_with_the_live_importer(string fileName)
    {
        var parsed = ParseExample(fileName);
        Assert.NotEmpty(parsed.Steps);
    }

    [Fact]
    public void Api_uptime_watchdog_keeps_its_branch_fanout_join_shape()
    {
        var parsed = ParseExample("api-uptime-watchdog.automatex.json");

        Assert.True(parsed.ContinueOnFailure);
        Assert.Equal(7, parsed.Steps.Count);
        Assert.Equal("switch", parsed.Steps[1].ActionType);

        // switch fork: an "up" lane and the "default" lane.
        Assert.Contains(parsed.Edges, e => e.FromOrder == 1 && e.Label == "up");
        Assert.Contains(parsed.Edges, e => e.FromOrder == 1 && e.Label == "default");

        // diagnose (3) fans out into two unconditional lanes …
        Assert.Contains(parsed.Edges, e => e is { FromOrder: 3, ToOrder: 4, Label: null });
        Assert.Contains(parsed.Edges, e => e is { FromOrder: 3, ToOrder: 5, Label: null });

        // … that rejoin on the incident step (6).
        Assert.Equal(2, parsed.Edges.Count(e => e.ToOrder == 6));

        Assert.Contains(parsed.Triggers, t => t.Type == "cron");
    }

    private static WorkflowTransfer.ParsedImport ParseExample(string fileName)
    {
        var document = JsonNode.Parse(File.ReadAllText(Path.Combine(ExamplesDirectory(), fileName))) as JsonObject
            ?? throw new InvalidOperationException($"{fileName} is not a JSON object.");
        return WorkflowTransfer.Parse(document);
    }

    // /examples sits at the repo root, two levels up from this test file (tests/AutomateX.Tests).
    // CallerFilePath resolves at compile time, so this is independent of the runtime working dir.
    private static string ExamplesDirectory([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", "examples"));
}
