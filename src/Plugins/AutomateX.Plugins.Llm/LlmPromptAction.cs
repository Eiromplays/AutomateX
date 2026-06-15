using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutomateX.Plugin.Sdk;

namespace AutomateX.Plugins.Llm;

public sealed record LlmPromptConfig(
    string Model,
    [property: Multiline] string Prompt,
    string? ApiKey = null,
    [property: Multiline] string? System = null,
    string BaseUrl = "https://api.openai.com",
    double? Temperature = null,
    int? MaxTokens = null);

public sealed record LlmPromptResult(string Text, string Model, int? PromptTokens, int? CompletionTokens);

[Action("llm.prompt", "LLM: Prompt",
    Description = "Sends a prompt to any OpenAI-compatible chat-completions endpoint (OpenAI, OpenRouter, "
        + "Ollama, LM Studio, vLLM — set baseUrl; apiKey optional for local endpoints, use "
        + "{{connections.<name>.apiKey}}). The completion text lands in {{steps.<n>.output.text}}. "
        + "Note: engine retries re-send the prompt and re-bill tokens.")]
public sealed class LlmPromptAction : IAction<LlmPromptConfig, LlmPromptResult>
{
    public async Task<LlmPromptResult> ExecuteAsync(
        LlmPromptConfig config,
        ActionContext context,
        CancellationToken cancellationToken = default)
    {
        Validate(config);

        var messages = new JsonArray();
        if (config.System is { Length: > 0 })
        {
            messages.Add(new JsonObject { ["role"] = "system", ["content"] = config.System });
        }

        messages.Add(new JsonObject { ["role"] = "user", ["content"] = config.Prompt });

        var payload = new JsonObject
        {
            ["model"] = config.Model,
            ["messages"] = messages,
        };
        if (config.Temperature is { } temperature)
        {
            payload["temperature"] = temperature;
        }

        if (config.MaxTokens is { } maxTokens)
        {
            payload["max_tokens"] = maxTokens;
        }

        var url = $"{config.BaseUrl.TrimEnd('/')}/v1/chat/completions";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload),
        };
        if (config.ApiKey is { Length: > 0 })
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        }

        using var response = await context.Http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"llm.prompt failed: {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(body)}");
        }

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        if (root.TryGetProperty("choices", out var choices) is false || choices.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"llm.prompt got no choices back: {Truncate(body)}");
        }

        var text = choices[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        var model = root.TryGetProperty("model", out var modelProp) ? modelProp.GetString() ?? config.Model : config.Model;

        int? promptTokens = null;
        int? completionTokens = null;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var pt) && pt.TryGetInt32(out var p))
            {
                promptTokens = p;
            }

            if (usage.TryGetProperty("completion_tokens", out var ct) && ct.TryGetInt32(out var c))
            {
                completionTokens = c;
            }
        }

        return new LlmPromptResult(text, model, promptTokens, completionTokens);
    }

    private static void Validate(LlmPromptConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Model))
        {
            throw new ArgumentException("llm.prompt requires 'model'.");
        }

        if (string.IsNullOrWhiteSpace(config.Prompt))
        {
            throw new ArgumentException("llm.prompt requires 'prompt'.");
        }

        if (string.IsNullOrWhiteSpace(config.BaseUrl))
        {
            throw new ArgumentException("llm.prompt requires 'baseUrl'.");
        }
    }

    private static string Truncate(string value) =>
        value.Length <= 1000 ? value.Trim() : value[..1000].Trim() + "…";
}
