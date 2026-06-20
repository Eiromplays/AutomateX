using AutomateX.Modules.Workflows;
using Xunit;

namespace AutomateX.Tests;

// Save-time guard: a step reference that can never resolve (unknown key, out-of-range order)
// is rejected. Valid keys/orders and non-step tokens pass untouched.
public sealed class StepReferencesTests
{
    private static List<string> Collect(params StepDefinition[] steps)
    {
        var errors = new List<string>();
        StepReferences.Validate(steps, errors.Add);
        return errors;
    }

    [Fact]
    public void Valid_key_reference_passes()
    {
        var errors = Collect(
            new StepDefinition("ssh.command", "SSH deploy", "{}"),
            new StepDefinition("matrix.send", "Announce", """{"message":"{{steps.ssh-deploy.output.stdout}}"}"""));

        Assert.Empty(errors);
    }

    [Fact]
    public void Valid_numeric_reference_passes()
    {
        var errors = Collect(
            new StepDefinition("ssh.command", "Deploy", "{}"),
            new StepDefinition("matrix.send", "Announce", """{"message":"{{steps.0.output.stdout}}"}"""));

        Assert.Empty(errors);
    }

    [Fact]
    public void Unknown_key_reference_is_rejected()
    {
        var errors = Collect(
            new StepDefinition("ssh.command", "SSH deploy", "{}"),
            new StepDefinition("matrix.send", "Announce", """{"message":"{{steps.nope.output.stdout}}"}"""));

        Assert.Contains(errors, e => e.Contains("nope"));
    }

    [Fact]
    public void Out_of_range_numeric_reference_is_rejected()
    {
        var errors = Collect(
            new StepDefinition("ssh.command", "Deploy", "{}"),
            new StepDefinition("matrix.send", "Announce", """{"message":"{{steps.5.output.stdout}}"}"""));

        Assert.Contains(errors, e => e.Contains('5'));
    }

    [Fact]
    public void Non_step_tokens_are_ignored()
    {
        var errors = Collect(
            new StepDefinition("http.request", "Fetch",
                """{"url":"{{trigger.payload.url}}","auth":"{{connections.gh.token}}"}"""));

        Assert.Empty(errors);
    }
}
