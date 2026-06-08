using System.Text.Json;
using AutomateX.Engine.Actions;
using Xunit;

namespace AutomateX.Tests;

public sealed class GateEvaluatorTests
{
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    private static GateConfig Config(string valueJson, string? eq = null, string? neq = null, string? contains = null, bool? truthy = null) =>
        new(Json(valueJson), eq, neq, contains, truthy);

    [Theory]
    [InlineData("\"go\"", "go", true)]
    [InlineData("\"stop\"", "go", false)]
    public void Equals_opens_only_on_match(string value, string eq, bool open) =>
        Assert.Equal(open, GateEvaluator.Evaluate(Config(value, eq: eq)).Open);

    [Fact]
    public void NotEquals_opens_when_different()
    {
        Assert.True(GateEvaluator.Evaluate(Config("\"a\"", neq: "b")).Open);
        Assert.False(GateEvaluator.Evaluate(Config("\"a\"", neq: "a")).Open);
    }

    [Fact]
    public void Contains_opens_on_substring()
    {
        Assert.True(GateEvaluator.Evaluate(Config("\"hello world\"", contains: "world")).Open);
        Assert.False(GateEvaluator.Evaluate(Config("\"hello\"", contains: "world")).Open);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("\"yes\"", true)]
    [InlineData("\"\"", false)]
    [InlineData("\"0\"", false)]
    [InlineData("0", false)]
    [InlineData("\"false\"", false)]
    [InlineData("null", false)]
    public void IsTruthy_reads_common_truthy_and_falsy_forms(string value, bool truthy)
    {
        Assert.Equal(truthy, GateEvaluator.Evaluate(Config(value, truthy: true)).Open);
        Assert.Equal(!truthy, GateEvaluator.Evaluate(Config(value, truthy: false)).Open);
    }

    [Fact]
    public void No_operator_defaults_to_truthiness()
    {
        Assert.True(GateEvaluator.Evaluate(Config("true")).Open);
        Assert.False(GateEvaluator.Evaluate(Config("false")).Open);
    }

    [Fact]
    public void Multiple_operators_are_anded()
    {
        // contains passes but equals fails → closed
        Assert.False(GateEvaluator.Evaluate(Config("\"abc\"", eq: "xyz", contains: "ab")).Open);
        // both pass → open
        Assert.True(GateEvaluator.Evaluate(Config("\"abc\"", eq: "abc", contains: "ab")).Open);
    }

    [Fact]
    public void Closed_reason_explains_why()
    {
        var result = GateEvaluator.Evaluate(Config("\"stop\"", eq: "go"));
        Assert.False(result.Open);
        Assert.Contains("go", result.Reason);
    }
}
