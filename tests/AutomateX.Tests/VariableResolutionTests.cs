using AutomateX.Modules.Variables;
using Xunit;

namespace AutomateX.Tests;

public sealed class VariableResolutionTests
{
    private static readonly Guid Prod = Guid.NewGuid();
    private static readonly Guid Default = Guid.NewGuid();

    private static VariableValueSet Var(
        string name, bool workflow, bool secret, params (Guid Env, string Value)[] values) =>
        new(name, workflow, secret, values.ToDictionary(v => v.Env, v => v.Value));

    [Fact]
    public void Workflow_scope_shadows_workspace_scope()
    {
        var (values, _) = VariableResolution.Resolve(
            [
                Var("region", workflow: false, secret: false, (Prod, "eu-workspace")),
                Var("region", workflow: true, secret: false, (Prod, "eu-workflow")),
            ],
            Prod,
            Default);

        Assert.Equal("eu-workflow", values["region"]);
    }

    [Fact]
    public void Falls_back_to_default_environment_when_chosen_env_has_no_value()
    {
        var (values, _) = VariableResolution.Resolve(
            [Var("baseUrl", workflow: false, secret: false, (Default, "https://default"))],
            Prod,
            Default);

        Assert.Equal("https://default", values["baseUrl"]);
    }

    [Fact]
    public void Variable_with_no_applicable_value_is_absent()
    {
        var (values, _) = VariableResolution.Resolve(
            [Var("ghost", workflow: false, secret: false, (Guid.NewGuid(), "other-env-only"))],
            Prod,
            Default);

        Assert.False(values.ContainsKey("ghost"));
    }

    [Fact]
    public void Secret_names_track_the_winning_variable()
    {
        var (_, secrets) = VariableResolution.Resolve(
            [
                Var("token", workflow: false, secret: true, (Prod, "sekret")),
                Var("plain", workflow: false, secret: false, (Prod, "open")),
            ],
            Prod,
            Default);

        Assert.Contains("token", secrets);
        Assert.DoesNotContain("plain", secrets);
    }

    [Fact]
    public void Plain_workflow_override_de_secrets_a_shadowed_secret()
    {
        var (values, secrets) = VariableResolution.Resolve(
            [
                Var("apiKey", workflow: false, secret: true, (Prod, "secret-value")),
                Var("apiKey", workflow: true, secret: false, (Prod, "public-value")),
            ],
            Prod,
            Default);

        Assert.Equal("public-value", values["apiKey"]);
        Assert.DoesNotContain("apiKey", secrets);
    }
}
