using System.Text.Json;
using AutomateX.Engine.Actions;
using Xunit;

namespace AutomateX.Tests;

public sealed class SwitchEvaluatorTests
{
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    private static SwitchConfig Config(string valueJson, params SwitchCase[] cases) =>
        new(Json(valueJson), [.. cases]);

    [Fact]
    public void First_matching_case_wins()
    {
        var config = Config("\"b\"",
            new SwitchCase("first", EqualsValue: "a"),
            new SwitchCase("second", EqualsValue: "b"),
            new SwitchCase("third", Contains: "b"));

        Assert.Equal("second", SwitchEvaluator.Evaluate(config).Label);
    }

    [Theory]
    [InlineData("\"go\"", "matched")]
    [InlineData("\"stop\"", "default")]
    public void Equals_selects_or_falls_through_to_default(string value, string expected)
    {
        var config = Config(value, new SwitchCase("matched", EqualsValue: "go"));
        Assert.Equal(expected, SwitchEvaluator.Evaluate(config).Label);
    }

    [Fact]
    public void Each_operator_kind_can_select_a_case()
    {
        Assert.Equal("ne", SwitchEvaluator.Evaluate(Config("\"x\"", new SwitchCase("ne", NotEquals: "y"))).Label);
        Assert.Equal("ct", SwitchEvaluator.Evaluate(Config("\"hello world\"", new SwitchCase("ct", Contains: "world"))).Label);
        Assert.Equal("ty", SwitchEvaluator.Evaluate(Config("true", new SwitchCase("ty", IsTruthy: true))).Label);
    }

    [Fact]
    public void No_matching_case_returns_the_default_label()
    {
        var config = Config("\"none\"",
            new SwitchCase("a", EqualsValue: "x"),
            new SwitchCase("b", EqualsValue: "y"));

        Assert.Equal(Switch.DefaultLabel, SwitchEvaluator.Evaluate(config).Label);
    }

    [Fact]
    public void Empty_cases_returns_default()
    {
        Assert.Equal(Switch.DefaultLabel, SwitchEvaluator.Evaluate(Config("\"anything\"")).Label);
    }

    [Fact]
    public void Operatorless_case_matches_on_truthiness()
    {
        // A case with no operator inherits the gate's "is truthy" default.
        Assert.Equal("catch", SwitchEvaluator.Evaluate(Config("\"non-empty\"", new SwitchCase("catch"))).Label);
        Assert.Equal(Switch.DefaultLabel, SwitchEvaluator.Evaluate(Config("\"\"", new SwitchCase("catch"))).Label);
    }

    [Fact]
    public void ChosenLabel_reads_label_from_output_json()
    {
        Assert.Equal("ok", Switch.ChosenLabel("{\"label\":\"ok\"}"));
        Assert.Null(Switch.ChosenLabel("{\"open\":true}"));
        Assert.Null(Switch.ChosenLabel(""));
        Assert.Null(Switch.ChosenLabel("not json"));
    }
}
