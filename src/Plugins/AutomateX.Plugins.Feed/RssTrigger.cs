using System.ServiceModel.Syndication;
using System.Text.Json;
using System.Xml;
using AutomateX.Plugin.Sdk;

namespace AutomateX.Plugins.Feed;

// PollSeconds is the delay between polls (0 = continuous; for real feeds use >= 60).
// DedupTtlDays bounds the "seen" set so old item ids eventually expire.
public sealed record RssTriggerConfig(
    string Url,
    int PollSeconds = 300,
    int DedupTtlDays = 30,
    bool FireOnFirstPoll = false);

[Trigger("rss", "RSS / Atom feed",
    Description = "Polls an RSS or Atom feed and fires once per new item. The first poll establishes "
        + "a baseline silently (set fireOnFirstPoll to emit existing items instead). Dedup is durable, "
        + "so restarts never replay. Payload: id, title, link, summary, published, authors.")]
public sealed class RssTrigger : ITriggerListener<RssTriggerConfig>
{
    public async Task RunAsync(RssTriggerConfig config, TriggerContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.Url))
        {
            throw new ArgumentException("rss requires 'url'.");
        }

        var dedupTtl = TimeSpan.FromDays(Math.Max(1, config.DedupTtlDays));

        while (!cancellationToken.IsCancellationRequested)
        {
            var firstPoll = await context.State.GetAsync("baseline", cancellationToken) is null;

            foreach (var item in await FetchItemsAsync(context.Http, config.Url, cancellationToken))
            {
                var id = ItemId(item);
                var isNew = await context.MarkNewAsync($"item:{id}", dedupTtl);
                if (isNew && (!firstPoll || config.FireOnFirstPoll))
                {
                    await context.FireAsync(Payload(item, id));
                }
            }

            if (firstPoll)
            {
                await context.State.SetAsync("baseline", "1", cancellationToken: cancellationToken);
            }

            await Task.Delay(TimeSpan.FromSeconds(config.PollSeconds), cancellationToken);
        }
    }

    private static async Task<List<SyndicationItem>> FetchItemsAsync(
        HttpClient http, string url, CancellationToken cancellationToken)
    {
        // Bound the fetch ourselves — the shared trigger client is uncapped.
        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestCts.CancelAfter(TimeSpan.FromSeconds(30));

        var xml = await http.GetStringAsync(url, requestCts.Token);
        using var reader = XmlReader.Create(new StringReader(xml));
        return SyndicationFeed.Load(reader).Items.ToList();
    }

    // Prefer the stable feed id, then the first link, then a title+date fallback.
    private static string ItemId(SyndicationItem item) =>
        !string.IsNullOrEmpty(item.Id) ? item.Id
        : item.Links.FirstOrDefault()?.Uri?.ToString() is { Length: > 0 } link ? link
        : $"{item.Title?.Text}|{item.PublishDate.UtcDateTime:o}";

    private static string Payload(SyndicationItem item, string id) =>
        JsonSerializer.Serialize(new
        {
            id,
            title = item.Title?.Text,
            link = item.Links.FirstOrDefault()?.Uri?.ToString(),
            summary = item.Summary?.Text,
            published = item.PublishDate == default ? null : item.PublishDate.UtcDateTime.ToString("o"),
            authors = item.Authors
                .Select(a => string.IsNullOrEmpty(a.Name) ? a.Email : a.Name)
                .Where(name => !string.IsNullOrEmpty(name))
                .ToArray(),
        });
}
