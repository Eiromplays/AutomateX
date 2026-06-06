using System.Text.Json.Nodes;

namespace AutomateX.Engine.Actions;

public sealed record ActionDescriptor(
    string Type,
    string DisplayName,
    string? Description,
    string Source,
    JsonNode? ConfigSchema,
    JsonNode? ResultSchema);

public sealed record RegisteredAction(ActionDescriptor Descriptor, IActionExecutor Executor);

public interface IActionSource
{
    IEnumerable<RegisteredAction> GetActions();
}
