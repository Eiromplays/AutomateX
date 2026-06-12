using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutomateX.Engine.Mcp;
using AutomateX.Plugin.Sdk;
using Microsoft.Extensions.Logging;

namespace AutomateX.Engine.Agent;

public sealed record AgentMcpServer(string ServerUrl, string? Token = null);

public sealed record LlmAgentConfig(
    string Model,
    string Goal,
    List<AgentMcpServer> McpServers,
    string? ApiKey = null,
    string? System = null,
    string BaseUrl = "https://api.openai.com",
    int MaxIterations = 8,
    double? Temperature = null,
    int? MaxTokens = null,
    List<string>? AllowedTools = null);

public sealed record AgentToolTrace(string Tool, string Arguments, string Result, bool IsError);

public sealed record LlmAgentResult(string Output, bool Finished, int Iterations, List<AgentToolTrace> Transcript);

[Action("llm.agent", "LLM: Agent",
    Description = "A bounded agent: it pursues a goal by calling tools from the given MCP server(s) in a "
        + "reason→call→observe loop, up to maxIterations, then returns the answer plus a transcript of every "
        + "tool call. Uses an OpenAI-compatible endpoint (set baseUrl/apiKey, e.g. {{connections.<name>.apiKey}}). "
        + "Non-deterministic and re-billed on retry.")]
public sealed class LlmAgentAction : IAction<LlmAgentConfig, LlmAgentResult>
{
    private const int IterationCeiling = 25;

    public async Task<LlmAgentResult> ExecuteAsync(
        LlmAgentConfig config, ActionContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(config.Model))
        {
            throw new ArgumentException("llm.agent requires 'model'.");
        }

        if (string.IsNullOrWhiteSpace(config.Goal))
        {
            throw new ArgumentException("llm.agent requires 'goal'.");
        }

        if (string.IsNullOrWhiteSpace(config.BaseUrl))
        {
            throw new ArgumentException("llm.agent requires 'baseUrl'.");
        }

        var client = new McpClient(context.Http);
        var (toolServers, openAiTools) = await BuildToolsAsync(client, config, cancellationToken);

        var messages = new JsonArray();
        if (config.System is { Length: > 0 })
        {
            messages.Add(new JsonObject { ["role"] = "system", ["content"] = config.System });
        }

        messages.Add(new JsonObject { ["role"] = "user", ["content"] = config.Goal });

        List<AgentToolTrace> transcript = [];
        var maxIterations = Math.Clamp(config.MaxIterations <= 0 ? 8 : config.MaxIterations, 1, IterationCeiling);

        for (var iteration = 1; iteration <= maxIterations; iteration++)
        {
            var body = await CompleteAsync(context.Http, config, messages, openAiTools, cancellationToken);
            var turn = AgentProtocol.ParseTurn(body);

            if (turn.FinalContent is not null)
            {
                context.Logger.LogInformation("llm.agent finished in {Iterations} iteration(s)", iteration);
                return new LlmAgentResult(turn.FinalContent, Finished: true, iteration, transcript);
            }

            // The model asked for tools: append its message verbatim, then each tool's result.
            messages.Add(turn.AssistantMessage!);
            foreach (var call in turn.ToolCalls)
            {
                var (result, isError) = await RunToolAsync(client, toolServers, call, cancellationToken);
                messages.Add(AgentProtocol.ToolResultMessage(call.Id, result));
                transcript.Add(new AgentToolTrace(call.Name, call.ArgumentsJson, result, isError));
            }
        }

        context.Logger.LogWarning("llm.agent hit its {Max}-iteration cap without finishing", maxIterations);
        return new LlmAgentResult(Output: "", Finished: false, maxIterations, transcript);
    }

    private static async Task<(Dictionary<string, McpServer> ToolServers, JsonArray OpenAiTools)> BuildToolsAsync(
        McpClient client, LlmAgentConfig config, CancellationToken cancellationToken)
    {
        Dictionary<string, McpServer> toolServers = new(StringComparer.Ordinal);
        List<McpTool> tools = [];

        foreach (var configured in config.McpServers ?? [])
        {
            Dictionary<string, string> headers = [];
            if (!string.IsNullOrEmpty(configured.Token))
            {
                headers["Authorization"] = $"Bearer {configured.Token}";
            }

            var server = new McpServer(configured.ServerUrl, headers);
            foreach (var tool in await client.ListToolsAsync(server, cancellationToken))
            {
                if (config.AllowedTools is { Count: > 0 } allowed && !allowed.Contains(tool.Name))
                {
                    continue;
                }

                if (toolServers.TryAdd(tool.Name, server)) // first server wins on a name collision
                {
                    tools.Add(tool);
                }
            }
        }

        return (toolServers, AgentProtocol.ToOpenAiTools(tools));
    }

    // A tool failure (unknown tool, bad arguments, server/tool error) is fed back to the model
    // as the tool result rather than failing the step — the agent can read it and recover.
    private static async Task<(string Result, bool IsError)> RunToolAsync(
        McpClient client, IReadOnlyDictionary<string, McpServer> toolServers, ToolCall call, CancellationToken cancellationToken)
    {
        if (!toolServers.TryGetValue(call.Name, out var server))
        {
            return ($"Unknown tool '{call.Name}'.", true);
        }

        JsonElement arguments;
        try
        {
            arguments = JsonSerializer.Deserialize<JsonElement>(call.ArgumentsJson);
        }
        catch (JsonException)
        {
            return ("The tool arguments were not valid JSON.", true);
        }

        try
        {
            var result = await client.CallToolAsync(server, call.Name, arguments, cancellationToken);
            return (result.Text, result.IsError);
        }
        catch (McpException ex)
        {
            return (ex.Message, true);
        }
    }

    private static async Task<string> CompleteAsync(
        HttpClient http, LlmAgentConfig config, JsonArray messages, JsonArray tools, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["model"] = config.Model,
            ["messages"] = messages.DeepClone(),
        };
        if (tools.Count > 0)
        {
            payload["tools"] = tools.DeepClone();
        }

        if (config.Temperature is { } temperature)
        {
            payload["temperature"] = temperature;
        }

        if (config.MaxTokens is { } maxTokens)
        {
            payload["max_tokens"] = maxTokens;
        }

        var url = $"{config.BaseUrl.TrimEnd('/')}/v1/chat/completions";
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        if (config.ApiKey is { Length: > 0 })
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        }

        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"llm.agent LLM call failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        return body;
    }
}
