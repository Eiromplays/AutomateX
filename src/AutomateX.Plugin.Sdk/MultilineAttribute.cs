namespace AutomateX.Plugin.Sdk;

// Marks a string config property as multi-line. The builder renders a textarea instead of a
// single-line input; surfaces in the exported config schema as format:"multiline".
[AttributeUsage(AttributeTargets.Property)]
public sealed class MultilineAttribute : Attribute;
