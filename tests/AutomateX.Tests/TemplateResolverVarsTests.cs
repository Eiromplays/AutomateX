using System.Text.Json;
using AutomateX.Engine.Templating;
using Xunit;

namespace AutomateX.Tests;

public sealed class TemplateResolverVarsTests
{
    [Fact]
    public void Resolves_vars_and_captures_secret_reads_for_masking()
    {
        HashSet<string> sink = [];
        var context = new TemplateContext(
            null,
            new Dictionary<int, JsonElement>(),
            Guid.Empty,
            Guid.Empty,
            Variables: new Dictionary<string, string> { ["region"] = "eu", ["token"] = "s3cr3t" },
            SecretVariableNames: new HashSet<string> { "token" },
            SecretSink: sink);

        var result = TemplateResolver.Resolve("""{"r":"{{vars.region}}","t":"{{vars.token}}"}""", context);

        Assert.Contains("eu", result);
        Assert.Contains("s3cr3t", result);
        Assert.Contains("s3cr3t", sink); // secret var value captured → masked downstream
        Assert.DoesNotContain("eu", sink); // plain var not masked
    }

    [Fact]
    public void Unknown_var_throws_in_strict_mode()
    {
        var context = new TemplateContext(
            null, new Dictionary<int, JsonElement>(), Guid.Empty, Guid.Empty,
            Variables: new Dictionary<string, string>());

        Assert.Throws<TemplateResolutionException>(() =>
            TemplateResolver.Resolve("""{"x":"{{vars.missing}}"}""", context));
    }
}
