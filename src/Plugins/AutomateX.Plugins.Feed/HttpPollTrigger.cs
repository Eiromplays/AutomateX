using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutomateX.Plugin.Sdk;

namespace AutomateX.Plugins.Feed;

// PollSeconds is the delay between polls (0 = continuous; for real endpoints use >= 60).
// Headers are sent on every request — e.g. { "Authorization": "Bearer {{connections.gh.token}}" }
// for a private API, or a custom Accept.
public sealed record HttpPollTriggerConfig(
    string Url,
    int PollSeconds = 300,
    bool FireOnFirstPoll = false,
    Dictionary<string, string>? Headers = null);

[Trigger("http.poll", "HTTP poll",
    Description = "Polls a URL and fires when a successful (2xx) response body changes (dedup by content "
        + "hash). Non-2xx responses are ignored — they never fire and never reset the baseline, so a "
        + "flapping error page can't trigger. Optional headers (e.g. Authorization). The first poll is a "
        + "silent baseline unless fireOnFirstPoll is set. Payload: statusCode, body, and json (the parsed "
        + "body when it's JSON) — template it as {{trigger.payload.json.<path>}}.")]
public sealed class HttpPollTrigger : ITriggerListener<HttpPollTriggerConfig>
{
    public async Task RunAsync(HttpPollTriggerConfig config, TriggerContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.Url))
        {
            throw new ArgumentException("http.poll requires 'url'.");
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestCts.CancelAfter(TimeSpan.FromSeconds(30));

            using var request = new HttpRequestMessage(HttpMethod.Get, config.Url);
            foreach (var (name, value) in config.Headers ?? [])
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }

            using var response = await context.Http.SendAsync(request, requestCts.Token);

            // Only a successful response counts as content. A non-2xx (auth error, rate limit, 404)
            // is skipped entirely: it never fires and never overwrites the baseline, so error-page
            // churn can't masquerade as "the watched thing changed".
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(requestCts.Token);
                var hash = Hash(body);

                var previous = await context.State.GetAsync("hash", cancellationToken);
                if (previous != hash)
                {
                    if (previous is not null || config.FireOnFirstPoll)
                    {
                        await context.FireAsync(Payload((int)response.StatusCode, body));
                    }

                    await context.State.SetAsync("hash", hash, cancellationToken: cancellationToken);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(config.PollSeconds), cancellationToken);
        }
    }

    private static string Hash(string body) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));

    // Payload exposes the raw body and, when it parses as JSON, the parsed tree under `json` —
    // so steps can template into it: {{trigger.payload.json.tag_name}} or, for an array response,
    // {{trigger.payload.json.0.tag_name}}. Non-JSON bodies leave json null.
    private static string Payload(int statusCode, string body)
    {
        JsonNode? json = null;
        try
        {
            json = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            // Body isn't JSON — only the raw string is available.
        }

        return JsonSerializer.Serialize(new { statusCode, body, json });
    }
}
