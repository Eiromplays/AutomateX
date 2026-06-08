using AutomateX.Plugin.Sdk;

namespace AutomateX.Plugins.Llm;

[ConnectionType("llm", "LLM (OpenAI-compatible)",
    Description = "An API key for any OpenAI-compatible endpoint. Local endpoints (Ollama) need none.")]
public sealed class LlmConnectionType : IConnectionType
{
    public IReadOnlyList<ConnectionField> Fields { get; } =
    [
        new("apiKey", "API key", Required: false,
            HelpText: "Your provider's API key (OpenAI, OpenRouter, …). Leave blank for local endpoints like Ollama."),
    ];
}
