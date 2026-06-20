using AutomateX.Modules.Workflows;
using Xunit;

namespace AutomateX.Tests;

// Step keys are the stable reference identity: derived from the display name, unique per
// version, positionally fallback'd, and independent of order (so inserts/reorders don't
// re-point references — the regression this closes).
public sealed class StepKeyTests
{
    [Theory]
    [InlineData("SSH deploy", 0, "ssh-deploy")]
    [InlineData("Probe API", 0, "probe-api")]
    [InlineData("  Send   Notification!! ", 0, "send-notification")]
    [InlineData("Healthy?", 0, "healthy")]
    [InlineData("café_übung", 0, "caf-bung")]
    public void Slugify_derives_a_slug_from_the_name(string name, int order, string expected) =>
        Assert.Equal(expected, StepKey.Slugify(name, order));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("???")]
    public void Slugify_falls_back_to_position_when_unsluggable(string? name) =>
        Assert.Equal("step-3", StepKey.Slugify(name, 2));

    [Fact]
    public void Unique_appends_numeric_suffixes_on_collision()
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);

        Assert.Equal("send", StepKey.Unique("send", taken));
        Assert.Equal("send-2", StepKey.Unique("send", taken));
        Assert.Equal("send-3", StepKey.Unique("send", taken));
    }

    [Fact]
    public void AddVersion_assigns_keys_from_names()
    {
        var workflow = Workflow.Create("wf", null);

        var version = workflow.AddVersion([
            new StepDefinition("http.request", "Probe API", "{}"),
            new StepDefinition("switch", "Healthy?", "{}"),
        ]);

        Assert.Equal(["probe-api", "healthy"], version.Steps.OrderBy(s => s.Order).Select(s => s.Key));
    }

    [Fact]
    public void AddVersion_dedups_duplicate_names()
    {
        var workflow = Workflow.Create("wf", null);

        var version = workflow.AddVersion([
            new StepDefinition("http.request", "Notify", "{}"),
            new StepDefinition("http.request", "Notify", "{}"),
            new StepDefinition("http.request", "Notify", "{}"),
        ]);

        Assert.Equal(["notify", "notify-2", "notify-3"], version.Steps.OrderBy(s => s.Order).Select(s => s.Key));
    }

    [Fact]
    public void AddVersion_honours_an_explicit_key()
    {
        var workflow = Workflow.Create("wf", null);

        var version = workflow.AddVersion([
            new StepDefinition("ssh.command", "Deploy the thing", "{}", Key: "deploy"),
        ]);

        Assert.Equal("deploy", version.Steps.Single().Key);
    }

    [Fact]
    public void Keys_are_independent_of_order()
    {
        var workflow = Workflow.Create("wf", null);

        // Insert a new step at the front; the existing steps keep their keys.
        var version = workflow.AddVersion([
            new StepDefinition("kv.setIfAbsent", "Dedup", "{}"),
            new StepDefinition("ssh.command", "SSH deploy", "{}"),
            new StepDefinition("matrix.send", "Announce", "{}"),
        ]);

        var keyByName = version.Steps.ToDictionary(s => s.Name!, s => s.Key);
        Assert.Equal("ssh-deploy", keyByName["SSH deploy"]);
        Assert.Equal("announce", keyByName["Announce"]);
    }
}
