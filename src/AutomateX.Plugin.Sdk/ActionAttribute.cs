namespace AutomateX.Plugin.Sdk;

[AttributeUsage(AttributeTargets.Class)]
public sealed class ActionAttribute(string type, string displayName) : Attribute
{
    public string Type { get; } = type;

    public string DisplayName { get; } = displayName;

    public string? Description { get; init; }
}
