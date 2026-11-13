using System.Collections.Concurrent;
using System.Text.Json;
using OsuRotate.Application;
using OsuRotate.Domain;

namespace OsuRotate.Infrastructure.OsuApi;

// IBeatmapCatalog over GET /api/v2/beatmapsets/lookup, ported from osu-ahr's BeatmapRepository: one
// request brings the whole set with its converts, and every map in it is cached for a day.
public sealed class OsuBeatmapCatalog(OsuApiClient api, TimeProvider? time = null) : IBeatmapCatalog
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromHours(24);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<(int Id, GameMode Mode), Cached> _cache = new();
    private readonly ConcurrentDictionary<int, DateTimeOffset> _unavailable = new();

    private long _prunedAt;

    public int CachedMaps => _cache.Count;

    public int UnavailableMaps => _unavailable.Count;

    public async ValueTask<BeatmapLookup> FindAsync(int beatmapId, GameMode mode, bool allowConvert, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        if (Pick(beatmapId, mode, allowConvert, now) is { } cached)
        {
            return cached;
        }

        Prune(now);
        var result = await api.GetAsync($"/api/v2/beatmapsets/lookup?beatmap_id={beatmapId}", cancellationToken).ConfigureAwait(false);
        using var body = result.Body;
        switch (result.Outcome)
        {
            case ApiOutcome.NotFound:
                return new BeatmapLookup(BeatmapLookupOutcome.NotFound, null);
            case ApiOutcome.Failed:
                return new BeatmapLookup(BeatmapLookupOutcome.Failed, null);
        }

        try
        {
            var set = body!.RootElement;
            if (IsUnavailable(set))
            {
                foreach (var map in Maps(set))
                {
                    _unavailable[map.Id] = now;
                }

                return new BeatmapLookup(BeatmapLookupOutcome.NotAvailable, null);
            }

            foreach (var map in Maps(set))
            {
                _cache[(map.Id, map.Info.Mode)] = map with { FetchedAt = now };
            }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return new BeatmapLookup(BeatmapLookupOutcome.Failed, null);
        }

        return Pick(beatmapId, mode, allowConvert, now) ?? new BeatmapLookup(BeatmapLookupOutcome.ModeMismatch, null);
    }

    // A lobby that runs for months looks up thousands of sets and plays few of them twice. An entry
    // was only dropped when the same map was asked for again, so the rest stayed for good. Now and
    // then, everything past its day is dropped.
    private void Prune(DateTimeOffset now)
    {
        var last = Interlocked.Read(ref _prunedAt);
        if (now.UtcTicks - last < PruneEvery.Ticks || Interlocked.CompareExchange(ref _prunedAt, now.UtcTicks, last) != last)
        {
            return;
        }

        foreach (var entry in _cache)
        {
            if (now - entry.Value.FetchedAt >= CacheFor)
            {
                _cache.TryRemove(entry);
            }
        }

        foreach (var entry in _unavailable)
        {
            if (now - entry.Value >= CacheFor)
            {
                _unavailable.TryRemove(entry);
            }
        }
    }

    private static readonly TimeSpan PruneEvery = TimeSpan.FromHours(1);

    private BeatmapLookup? Pick(int beatmapId, GameMode mode, bool allowConvert, DateTimeOffset now)
    {
        if (_unavailable.TryGetValue(beatmapId, out var seen) && now - seen < CacheFor)
        {
            return new BeatmapLookup(BeatmapLookupOutcome.NotAvailable, null);
        }

        if (!_cache.TryGetValue((beatmapId, mode), out var cached))
        {
            return null;
        }

        if (now - cached.FetchedAt >= CacheFor)
        {
            _cache.TryRemove((beatmapId, mode), out _);
            return null;
        }

        // A convert only counts when converts are allowed; osu! standard has none.
        return cached.IsConvert && !allowConvert ? new BeatmapLookup(BeatmapLookupOutcome.ModeMismatch, null) : BeatmapLookup.Found(cached.Info);
    }

    // osu-ahr's rule: no download, or a note about why there is none.
    private static bool IsUnavailable(JsonElement set) =>
        set.TryGetProperty("availability", out var availability)
        && ((availability.TryGetProperty("download_disabled", out var disabled) && disabled.ValueKind == JsonValueKind.True)
            || (availability.TryGetProperty("more_information", out var more) && more.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)));

    private static IEnumerable<Cached> Maps(JsonElement set)
    {
        var setId = set.GetProperty("id").GetInt32();
        var title = set.GetProperty("title").GetString() ?? string.Empty;
        foreach (var (property, isConvert) in new[] { ("beatmaps", false), ("converts", true) })
        {
            if (!set.TryGetProperty(property, out var maps) || maps.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var map in maps.EnumerateArray())
            {
                var info = new BeatmapInfo(
                    map.GetProperty("id").GetInt32(),
                    setId,
                    title,
                    (GameMode)map.GetProperty("mode_int").GetInt32(),
                    map.GetProperty("difficulty_rating").GetDouble(),
                    map.GetProperty("total_length").GetInt32(),
                    map.TryGetProperty("status", out var status) ? status.GetString() ?? string.Empty : string.Empty);
                yield return new Cached(info.Id, info, isConvert || (map.TryGetProperty("convert", out var convert) && convert.ValueKind == JsonValueKind.True), default);
            }
        }
    }

    private sealed record Cached(int Id, BeatmapInfo Info, bool IsConvert, DateTimeOffset FetchedAt);
}
