using System.Text.Json;
using System.Text.Json.Serialization;
using AutomateX.Plugin.Sdk;
using Microsoft.Extensions.Logging;

namespace AutomateX.Engine.Actions;

// EqualsValue is named to avoid colliding with the record's generated Equals method;
// its wire/schema name stays "equals".
public sealed record GateConfig(
    JsonElement Value,
    [property: JsonPropertyName("equals")] string? EqualsValue = null,
    string? NotEquals = null,
    string? Contains = null,
    bool? IsTruthy = null);

public sealed record GateResult(bool Open, string Reason);

// The condition logic — pure and testable. The engine reads `Open` from the step
// output and halts the workflow when it's false (see ExecuteStepHandler).
public static class GateEvaluator
{
    public static GateResult Evaluate(GateConfig config)
    {
        var value = Stringify(config.Value);
        List<(bool Ok, string Desc)> checks = [];

        if (config.EqualsValue is not null)
        {
            checks.Add((value == config.EqualsValue, $"equals '{config.EqualsValue}'"));
        }

        if (config.NotEquals is not null)
        {
            checks.Add((value != config.NotEquals, $"does not equal '{config.NotEquals}'"));
        }

        if (config.Contains is not null)
        {
            checks.Add((value.Contains(config.Contains, StringComparison.Ordinal), $"contains '{config.Contains}'"));
        }

        if (config.IsTruthy is { } truthy)
        {
            checks.Add((IsTruthy(value) == truthy, truthy ? "is truthy" : "is falsy"));
        }

        if (checks.Count == 0)
        {
            checks.Add((IsTruthy(value), "is truthy"));
        }

        var open = checks.All(x => x.Ok);
        var reason = open
            ? "open"
            : "closed — failed: " + string.Join(", ", checks.Where(x => !x.Ok).Select(x => x.Desc));

        return new GateResult(open, reason);
    }

    private static string Stringify(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null or JsonValueKind.Undefined => "",
        _ => value.GetRawText(),
    };

    private static bool IsTruthy(string value) =>
        value.Length > 0
        && !value.Equals("false", StringComparison.OrdinalIgnoreCase)
        && value != "0"
        && !value.Equals("null", StringComparison.OrdinalIgnoreCase);
}

// Engine-side helper: a gate step whose output says open=false halts the workflow.
public static class Gate
{
    public const string ActionType = "gate";

    public static bool IsClosed(string? outputJson)
    {
        if (string.IsNullOrEmpty(outputJson))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(outputJson);
            return doc.RootElement.TryGetProperty("open", out var open) && open.ValueKind == JsonValueKind.False;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

[Action("gate", "Gate",
    Description = "Stops the workflow unless the condition passes — later steps are skipped and the "
        + "execution still succeeds. Compare a (usually templated) value: equals / notEquals / contains, "
        + "or isTruthy. With no operator, opens when the value is truthy. E.g. value "
        + "{{steps.0.output.belowThreshold}}, isTruthy true → only continue when below threshold.")]
public sealed class GateAction : IAction<GateConfig, GateResult>
{
    public Task<GateResult> ExecuteAsync(GateConfig config, ActionContext context, CancellationToken cancellationToken = default)
    {
        var result = GateEvaluator.Evaluate(config);
        context.Logger.LogInformation("gate {State}: {Reason}", result.Open ? "open" : "closed", result.Reason);
        return Task.FromResult(result);
    }
}
