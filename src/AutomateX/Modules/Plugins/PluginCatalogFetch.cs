using System.Text;
using AutomateX.Database;
using AutomateX.Engine;
using AutomateX.Engine.Plugins;
using Microsoft.EntityFrameworkCore;

namespace AutomateX.Modules.Plugins;

// A fetched source: the parsed index + evaluated trust (SourceResult), the DB source id for provenance
// (null for the implicit first-party source), and any transport/parse error for the sources panel.
public sealed record FetchedSource(
    string Name, string Url, Guid? SourceId, CatalogSourceResult Result, string? Error);

// Shared catalog reading for browse (m3) and install (m4): the implicit first-party catalog from
// EngineOptions.PluginCatalogUrl plus the operator's enabled sources, each fetched, signature-verified, and
// trust-labelled. Kept static so it needs no DI registration — the endpoints already inject the inputs.
public static class PluginCatalogFetch
{
    public static async Task<List<FetchedSource>> ReadAllAsync(
        HttpClient http, EngineOptions options, AutomateXDbContext dbContext, CancellationToken ct)
    {
        List<FetchedSource> results = [];

        if (!string.IsNullOrWhiteSpace(options.PluginCatalogUrl))
        {
            results.Add(await ReadAsync(
                http, "AutomateX", options.PluginCatalogUrl, CatalogKeys.FirstParty, null, firstParty: true, ct));
        }

        var added = await dbContext.PluginCatalogSources
            .AsNoTracking()
            .Where(x => x.Enabled)
            .Select(x => new { x.Id, x.Name, x.Url, x.TrustedKey })
            .ToListAsync(ct);

        foreach (var source in added)
        {
            results.Add(await ReadAsync(http, source.Name, source.Url, source.TrustedKey, source.Id, firstParty: false, ct));
        }

        return results;
    }

    private static async Task<FetchedSource> ReadAsync(
        HttpClient http, string name, string url, string? key, Guid? sourceId, bool firstParty, CancellationToken ct)
    {
        try
        {
            var bytes = await http.GetByteArrayAsync(url, ct);
            var index = PluginCatalog.ParseIndex(Encoding.UTF8.GetString(bytes));

            string? signature = null;
            try
            {
                signature = await http.GetStringAsync(url + ".minisig", ct);
            }
            catch (HttpRequestException)
            {
                // Unsigned source.
            }

            var trust = CatalogSignature.Evaluate(bytes, signature, key);

            // The operator's own configured first-party catalog is trusted by configuration until the
            // release signing key ships (m7) — this preserves today's first-party install behaviour. Added
            // third-party sources stay Untrusted until their key is pinned.
            if (firstParty && key is null)
            {
                trust = CatalogTrust.Verified;
            }

            return new FetchedSource(name, url, sourceId, new CatalogSourceResult(name, trust, index), null);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            return new FetchedSource(name, url, sourceId, new CatalogSourceResult(name, CatalogTrust.Untrusted, null), exception.Message);
        }
    }
}

public sealed record ResolvedPlugin(FetchedSource Source, CatalogPlugin Plugin);

// Resolves a single plugin to install across sources: by id (preferred) or package name, skipping Blocked
// and unreachable sources, and — as in the browse aggregator — a Verified source wins over an Untrusted one
// (then the highest version), so an untrusted source can't hijack an install by claiming a higher version.
public static class PluginCatalogResolve
{
    public static ResolvedPlugin? Best(IEnumerable<FetchedSource> sources, string? id, string? name)
    {
        ResolvedPlugin? best = null;

        foreach (var source in sources)
        {
            if (source.Result.Index is null || source.Result.Trust == CatalogTrust.Blocked)
            {
                continue;
            }

            foreach (var plugin in source.Result.Index.Plugins)
            {
                if (!Matches(plugin.Manifest, id, name))
                {
                    continue;
                }

                var candidate = new ResolvedPlugin(source, plugin);
                if (best is null || Wins(candidate, best))
                {
                    best = candidate;
                }
            }
        }

        return best;
    }

    private static bool Matches(PluginManifest manifest, string? id, string? name) =>
        id is not null
            ? string.Equals(manifest.Id, id, StringComparison.OrdinalIgnoreCase)
            : name is not null && string.Equals(manifest.Name, name, StringComparison.OrdinalIgnoreCase);

    private static bool Wins(ResolvedPlugin candidate, ResolvedPlugin existing)
    {
        var trust = Rank(candidate.Source.Result.Trust).CompareTo(Rank(existing.Source.Result.Trust));
        return trust != 0
            ? trust > 0
            : SemVer.Compare(candidate.Plugin.Latest.Version, existing.Plugin.Latest.Version) > 0;
    }

    private static int Rank(CatalogTrust trust) => trust == CatalogTrust.Verified ? 1 : 0;
}
