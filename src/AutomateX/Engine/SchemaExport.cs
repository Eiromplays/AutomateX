using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using AutomateX.Plugin.Sdk;

namespace AutomateX.Engine;

// Shared JSON-Schema export for action/trigger config + result types. Beyond the stock exporter
// it stamps format:"multiline" onto [Multiline] properties so the builder renders a textarea.
// Returns null for types the exporter can't represent (matching the previous SchemaOrNull).
public static class SchemaExport
{
    private static readonly JsonSchemaExporterOptions Options = new()
    {
        TransformSchemaNode = static (context, node) =>
        {
            if (node is JsonObject schema
                && context.PropertyInfo?.AttributeProvider?.IsDefined(typeof(MultilineAttribute), inherit: true) == true)
            {
                schema["format"] = "multiline";
            }

            return node;
        },
    };

    public static JsonNode? ForType(Type type)
    {
        try
        {
            return JsonSerializerOptions.Web.GetJsonSchemaAsNode(type, Options);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
