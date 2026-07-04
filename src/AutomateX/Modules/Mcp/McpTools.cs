using System.Text;
using System.Text.Json;
using AutomateX.Modules.Executions;

namespace AutomateX.Modules.Mcp;

// Pure helpers for the MCP server: turning a workflow name into a stable tool name, and turning a
// settled execution into a tool result.
public static class McpTools
{
    // A safe tool identifier: lowercase, non-alphanumerics collapsed to underscores, trimmed. Used as
    // the default when a workflow doesn't set an explicit McpToolName.
    public static string Slug(string name)
    {
        var builder = new StringBuilder(name.Length);
        var lastUnderscore = false;
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastUnderscore = false;
            }
            else if (!lastUnderscore)
            {
                builder.Append('_');
                lastUnderscore = true;
            }
        }

        return builder.ToString().Trim('_') is { Length: > 0 } slug ? slug : "workflow";
    }

    public static bool IsTerminal(ExecutionStatus status) =>
        status is ExecutionStatus.Succeeded or ExecutionStatus.Failed;

    // The workflow's result = the output of the terminal step (highest order that succeeded). Callers
    // design an exposed workflow to end on the step whose output they want returned.
    public static string? TerminalOutput(IEnumerable<(int Order, ExecutionStatus Status, string? Output)> steps) =>
        steps
            .Where(x => x.Status == ExecutionStatus.Succeeded)
            .OrderByDescending(x => x.Order)
            .Select(x => x.Output)
            .FirstOrDefault();

    // Render a settled execution as MCP tool content: the terminal output (JSON if it parses, else the
    // raw string), or an error string for a failed run.
    public static JsonElement ResultContent(string? terminalOutput)
    {
        if (string.IsNullOrEmpty(terminalOutput))
        {
            return JsonSerializer.SerializeToElement("");
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(terminalOutput);
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(terminalOutput);
        }
    }
}
