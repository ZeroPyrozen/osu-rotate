using System.Globalization;
using System.Text.Json;
using OsuRotate.Application;
using OsuRotate.Domain;

namespace OsuRotate.Infrastructure.OsuApi;

// IMapPool over GET /api/v2/beatmapsets/search (map pick, issue 03). A fill searches with the limits,
// keeps every difficulty that really fits, and draws from them at random without handing a set out
// twice. The pool lives in memory only and is thrown away when the query changes.
//
// What the probe saw live (issue 01) shapes this: the search's filters match whole sets, so each
// difficulty is checked here; hybrid sets bring other modes; pages come by cursor only and the total
// is capped, so variety comes from a random sort order and skipping a few pages along the cursor.
public sealed class OsuMapPool(OsuApiClient api, Random? random = null) : IMapPool
{
    // The orders a fill picks from. Only the default, plays_desc and difficulty_asc were seen live.
    internal static readonly string[] Sorts = ["ranked_desc", "ranked_asc", "plays_desc", "favourites_desc", "difficulty_asc", "difficulty_desc"];

    // A page holds 50 sets, 70 to 200 fitting difficulties in the probe, so one kept page is enough.
    internal const int MaxPagesSkipped = 3;
    internal const int PagesKept = 1;

    private static readonly HashSet<string> Ranked = new(StringComparer.Ordinal) { "ranked", "approved" };

    private readonly Random _random = random ?? Random.Shared;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<BeatmapInfo> _pool = [];
    // Sets, not maps: the difficulties of one set are the same song, so a set is handed out once per
    // round. Live on 2026-11-15, two picks in a row came from one set when this held map ids.
    private readonly HashSet<int> _handedOutSets = [];
    private int _lastSetId;
    private MapPickQuery? _query;

    public async ValueTask<MapPickResult> DrawAsync(MapPickQuery query, IReadOnlySet<int> skip, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (query != _query)
            {
                _query = query;
                _pool.Clear();
                _handedOutSets.Clear();
            }

            // A map the room just played rules out its whole set.
            var skippedInPool = SetsOf(_pool, skip);
            _pool.RemoveAll(map => skippedInPool.Contains(map.BeatmapsetId));
            if (_pool.Count == 0)
            {
                if (await FillAsync(query, cancellationToken).ConfigureAwait(false) is not { } found)
                {
                    return new MapPickResult(MapPickOutcome.Failed);
                }

                var skippedSets = SetsOf(found, skip);
                var usable = found.Where(map => !skippedSets.Contains(map.BeatmapsetId)).DistinctBy(map => map.Id).ToList();
                var fresh = usable.Where(map => !_handedOutSets.Contains(map.BeatmapsetId)).ToList();

                // Every set the search gave was handed out already: start the round over, but not with
                // the set just handed out, unless it is the only one.
                if (fresh.Count == 0)
                {
                    _handedOutSets.Clear();
                    fresh = usable.Any(map => map.BeatmapsetId != _lastSetId)
                        ? [.. usable.Where(map => map.BeatmapsetId != _lastSetId)]
                        : usable;
                }

                _pool.AddRange(fresh);
            }

            if (_pool.Count == 0)
            {
                return new MapPickResult(MapPickOutcome.NoneFit);
            }

            // A random set, then a random difficulty in it, so a set with many fitting difficulties
            // comes up no more often than one with a single one.
            var sets = _pool.Select(map => map.BeatmapsetId).Distinct().ToList();
            var setId = sets[_random.Next(sets.Count)];
            var difficulties = _pool.Where(map => map.BeatmapsetId == setId).ToList();
            var picked = difficulties[_random.Next(difficulties.Count)];
            _pool.RemoveAll(map => map.BeatmapsetId == setId);
            _handedOutSets.Add(setId);
            _lastSetId = setId;
            return MapPickResult.Found(picked);
        }
        finally
        {
            _gate.Release();
        }
    }

    // The sets of the maps in skip that the pool knows; a skipped map from elsewhere has no known set.
    private static HashSet<int> SetsOf(IEnumerable<BeatmapInfo> maps, IReadOnlySet<int> skip) =>
        [.. maps.Where(map => skip.Contains(map.Id)).Select(map => map.BeatmapsetId)];

    // Null when the first page fails. A later failure keeps what was already seen, and so does a
    // search that ends before the skipped pages, so narrow rules still get their few maps.
    private async Task<List<BeatmapInfo>?> FillAsync(MapPickQuery query, CancellationToken cancellationToken)
    {
        var sort = Sorts[_random.Next(Sorts.Length)];
        var skipped = _random.Next(0, MaxPagesSkipped + 1);
        var seen = new List<BeatmapInfo>();
        var kept = new List<BeatmapInfo>();
        string? cursor = null;
        for (var page = 0; page < skipped + PagesKept; page++)
        {
            var result = await api.GetAsync(SearchPath(query, sort, cursor), cancellationToken).ConfigureAwait(false);
            using var body = result.Body;
            if (result.Outcome != ApiOutcome.Ok)
            {
                return page == 0 ? null : (kept.Count > 0 ? kept : seen);
            }

            List<BeatmapInfo> maps;
            try
            {
                (maps, cursor) = Read(body!.RootElement, query);
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                return page == 0 ? null : (kept.Count > 0 ? kept : seen);
            }

            seen.AddRange(maps);
            if (page >= skipped)
            {
                kept.AddRange(maps);
            }

            if (cursor is null)
            {
                break;
            }
        }

        return kept.Count > 0 ? kept : seen;
    }

    internal static string SearchPath(MapPickQuery query, string sort, string? cursor)
    {
        var filters = new List<string>();
        Filter(filters, "stars>=", query.StarMin);
        Filter(filters, "stars<=", query.StarMax);
        Filter(filters, "length>=", query.LengthMinSeconds);
        Filter(filters, "length<=", query.LengthMaxSeconds);
        filters.Add(string.Create(CultureInfo.InvariantCulture, $"ranked>={query.OldestRankedYear}"));

        var path = string.Create(CultureInfo.InvariantCulture,
            $"/api/v2/beatmapsets/search?q={Uri.EscapeDataString(string.Join(' ', filters))}&m={(int)query.Mode}&s=ranked&sort={sort}");
        return cursor is null ? path : $"{path}&cursor_string={Uri.EscapeDataString(cursor)}";
    }

    private static void Filter(List<string> filters, string name, double value)
    {
        if (value > 0)
        {
            filters.Add(string.Create(CultureInfo.InvariantCulture, $"{name}{value}"));
        }
    }

    private static (List<BeatmapInfo> Maps, string? Cursor) Read(JsonElement root, MapPickQuery query)
    {
        var maps = new List<BeatmapInfo>();
        foreach (var set in root.GetProperty("beatmapsets").EnumerateArray())
        {
            if (!Ranked.Contains(Text(set, "status") ?? string.Empty)
                || !DateTimeOffset.TryParse(Text(set, "ranked_date"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var ranked)
                || ranked.Year < query.OldestRankedYear
                || !set.TryGetProperty("beatmaps", out var difficulties) || difficulties.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var setId = set.GetProperty("id").GetInt32();
            var title = Text(set, "title") ?? string.Empty;
            foreach (var map in difficulties.EnumerateArray())
            {
                if (map.TryGetProperty("convert", out var convert) && convert.ValueKind == JsonValueKind.True)
                {
                    continue;
                }

                var status = Text(map, "status") ?? string.Empty;
                if (Mode(map) is not { } mode || mode != query.Mode || !Ranked.Contains(status))
                {
                    continue;
                }

                var info = new BeatmapInfo(
                    map.GetProperty("id").GetInt32(),
                    setId,
                    title,
                    mode,
                    map.GetProperty("difficulty_rating").GetDouble(),
                    map.GetProperty("total_length").GetInt32(),
                    status);
                if (query.Fits(info))
                {
                    maps.Add(info);
                }
            }
        }

        var cursor = root.TryGetProperty("cursor_string", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
        return (maps, cursor);
    }

    // The search names the mode; mode_int is read too in case only it is there.
    private static GameMode? Mode(JsonElement map)
    {
        if (map.TryGetProperty("mode_int", out var number) && number.ValueKind == JsonValueKind.Number)
        {
            return Enum.IsDefined((GameMode)number.GetInt32()) ? (GameMode)number.GetInt32() : null;
        }

        return Text(map, "mode") is { } name && GameModes.TryParse(name, out var mode) ? mode : null;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
