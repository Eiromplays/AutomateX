using System.Text.Json;
using System.Text.Json.Serialization;
using AutomateX.Plugin.Sdk;
using Microsoft.Extensions.Logging;

namespace AutomateX.Engine.Actions;

// A switch case reuses the gate's operator vocabulary against the switch's Value.
// EqualsValue avoids colliding with the record's generated Equals; its wire name stays "equals".
public sealed record SwitchCase(
    string Label,
    [property: JsonPropertyName("equals")] string? EqualsValue = null,
    string? NotEquals = null,
    string? Contains = null,
    bool? IsTruthy = null);

public sealed record SwitchConfig(JsonElement Value, List<SwitchCase> Cases);

public sealed record SwitchResult(string Label);

// The branch logic — pure and testable. The engine reads `Label` from the step output
// and routes to the outgoing edge carrying that label (see WorkflowRouter / ExecuteStep).
public static class SwitchEvaluator
{
    public static SwitchResult Evaluate(SwitchConfig config)
    {
        foreach (var branch in config.Cases ?? [])
        {
            var gate = GateEvaluator.Evaluate(
                new GateConfig(config.Value, branch.EqualsValue, branch.NotEquals, branch.Contains, branch.IsTruthy));
            if (gate.Open)
            {
                return new SwitchResult(branch.Label);
            }
        }

        return new SwitchResult(Switch.DefaultLabel);
    }
}

// Engine-side helper: a switch step's output names the label whose edge is taken.
public static class Switch
{
    public const string ActionType = "switch";
    public const string DefaultLabel = "default";

    public static string? ChosenLabel(string? outputJson)
    {
        if (string.IsNullOrEmpty(outputJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(outputJson);
            return doc.RootElement.TryGetProperty("label", out var label) && label.ValueKind == JsonValueKind.String
                ? label.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

[Action("switch", "Switch",
    Description = "Routes the workflow down one of several labelled paths. Compare a (usually templated) "
        + "value against ordered cases — the first case that passes (equals / notEquals / contains / isTruthy) "
        + "takes its labelled edge; no match takes the 'default' edge. Draw one outgoing edge per label in the "
        + "builder. E.g. value {{steps.0.output.status}}, case equals 'paid' → label 'paid'.")]
public sealed class SwitchAction : IAction<SwitchConfig, SwitchResult>
{
    public Task<SwitchResult> ExecuteAsync(SwitchConfig config, ActionContext context, CancellationToken cancellationToken = default)
    {
        var result = SwitchEvaluator.Evaluate(config);
        context.Logger.LogInformation("switch → {Label}", result.Label);
        return Task.FromResult(result);
    }
}
