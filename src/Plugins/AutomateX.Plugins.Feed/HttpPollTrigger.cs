using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutomateX.Plugin.Sdk;

namespace AutomateX.Plugins.Feed;

// PollSeconds is the delay between polls (0 = continuous; for real endpoints use >= 60).
public sealed record HttpPollTriggerConfig(
    string Url,
    int PollSeconds = 300,
    bool FireOnFirstPoll = false);

[Trigger("http.poll", "HTTP poll",
    Description = "Polls a URL and fires when the response body changes (dedup by content hash). "
        + "The first poll is a silent baseline unless fireOnFirstPoll is set. Payload: statusCode, body.")]
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

            using var response = await context.Http.GetAsync(config.Url, requestCts.Token);
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

            await Task.Delay(TimeSpan.FromSeconds(config.PollSeconds), cancellationToken);
        }
    }

    private static string Hash(string body) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));

    private static string Payload(int statusCode, string body) =>
        JsonSerializer.Serialize(new { statusCode, body });
}
