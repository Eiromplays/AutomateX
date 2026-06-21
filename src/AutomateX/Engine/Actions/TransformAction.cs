using System.Text.Json;
using AutomateX.Plugin.Sdk;
using JsonCons.JmesPath;

namespace AutomateX.Engine.Actions;

// Input is whatever JSON the (templated) value resolves to — typically {{steps.<key>.output}}.
// Query is a JMESPath expression that reshapes/extracts it. The result becomes the step output
// directly (no wrapper), so downstream refs are {{steps.<key>.output.<...>}} on the new shape.
public sealed record TransformConfig(JsonElement Input, string Query);

[Action("transform", "Transform (JMESPath)",
    Description = "Reshape or extract JSON with a JMESPath query. Set input to the value to transform "
        + "(e.g. {{steps.<key>.output}}) and query to a JMESPath expression — e.g. items[?ok].id or "
        + "{count: length(items), ids: items[].id}. See jmespath.org.")]
public sealed class TransformAction : IAction<TransformConfig, JsonElement>
{
    public Task<JsonElement> ExecuteAsync(
        TransformConfig config, ActionContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(config.Query))
        {
            throw new ArgumentException("transform requires a 'query' (a JMESPath expression).");
        }

        JsonDocument result;
        try
        {
            result = JsonTransformer.Transform(config.Input, config.Query);
        }
        catch (JmesPathParseException ex)
        {
            // A malformed expression is a deterministic authoring error — surface it clearly.
            throw new ArgumentException($"transform 'query' is not valid JMESPath: {ex.Message}");
        }

        // Clone detaches the value from the JsonDocument so it stays valid after disposal.
        using (result)
        {
            return Task.FromResult(result.RootElement.Clone());
        }
    }
}
