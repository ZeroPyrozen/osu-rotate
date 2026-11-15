using System.Globalization;
using System.Net;
using OsuRotate.Domain;
using OsuRotate.Infrastructure.OsuApi;

namespace OsuRotate.Infrastructure.Tests;

// Map pick, issue 03: the pool behind !pick, over GET /api/v2/beatmapsets/search. The search's shape
// and quirks are the ones the probe saw live (issue 01): filters match whole sets, hybrid sets bring
// other modes, and pages come by cursor_string.
public sealed class MapPoolTests : IDisposable
{
    private static readonly MapPickQuery Query = new(GameMode.Osu, 4, 5.5, 0, 300, 2012);
    private static readonly HashSet<int> NoSkip = [];

    private readonly FakeOsuApi _api = new();
    private readonly HttpClient _http;
    private readonly OsuApiClient _client;
    private readonly ScriptedRandom _random = new();

    public MapPoolTests()
    {
        _http = new HttpClient(_api);
        _client = new OsuApiClient(
            _http,
            new OsuApiOptions { ClientId = 7, ClientSecret = "secret", RetryDelay = TimeSpan.FromMilliseconds(1) },
            new ListLogger<OsuApiClient>());
    }

    public void Dispose()
    {
        _client.Dispose();
        _http.Dispose();
        _api.Dispose();
    }

    private OsuMapPool Pool() => new(_client, _random);

    // Returns the scripted values in turn, then 0. Next(n) is clamped so a script can't go out of range.
    private sealed class ScriptedRandom : Random
    {
        public Queue<int> Script { get; } = new();

        public override int Next(int maxValue) => Math.Min(Script.TryDequeue(out var value) ? value : 0, maxValue - 1);

        public override int Next(int minValue, int maxValue) => Math.Clamp(Script.TryDequeue(out var value) ? value : minValue, minValue, maxValue - 1);
    }

    private static string Path(MapPickQuery query, string sort = "ranked_desc", string? cursor = null)
    {
        var parts = new List<string>();
        if (query.StarMin > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"stars>={query.StarMin}"));
        }

        if (query.StarMax > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"stars<={query.StarMax}"));
        }

        if (query.LengthMinSeconds > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"length>={query.LengthMinSeconds}"));
        }

        if (query.LengthMaxSeconds > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"length<={query.LengthMaxSeconds}"));
        }

        parts.Add(string.Create(CultureInfo.InvariantCulture, $"ranked>={query.OldestRankedYear}"));
        var path = string.Create(CultureInfo.InvariantCulture, $"/api/v2/beatmapsets/search?q={Uri.EscapeDataString(string.Join(' ', parts))}&m={(int)query.Mode}&s=ranked&sort={sort}");
        return cursor is null ? path : $"{path}&cursor_string={Uri.EscapeDataString(cursor)}";
    }

    private void Serve(string path, string? next, params string[] sets) =>
        _api.Routes[path] = $$"""{"beatmapsets":[{{string.Join(',', sets)}}],"cursor_string":{{(next is null ? "null" : $"\"{next}\"")}},"total":10000}""";

    private static string Set(int id, string title, int rankedYear, string status, params string[] maps) =>
        $$"""{"id":{{id}},"title":"{{title}}","status":"{{status}}","ranked_date":"{{rankedYear}}-06-01T00:00:00Z","beatmaps":[{{string.Join(',', maps)}}]}""";

    private static string Map(int id, double stars, int length = 120, string mode = "osu", string status = "ranked") =>
        string.Create(CultureInfo.InvariantCulture, $$"""{"id":{{id}},"mode":"{{mode}}","difficulty_rating":{{stars}},"total_length":{{length}},"status":"{{status}}"}""");

    private async Task<List<int>> DrawIdsAsync(OsuMapPool pool, int times, IReadOnlySet<int>? skip = null)
    {
        var ids = new List<int>();
        for (var i = 0; i < times; i++)
        {
            var result = await pool.DrawAsync(Query, skip ?? NoSkip, default);
            Assert.Equal(MapPickOutcome.Found, result.Outcome);
            ids.Add(result.Beatmap!.Id);
        }

        return ids;
    }

    [Fact]
    public async Task Searches_with_the_limits_and_keeps_only_difficulties_that_fit()
    {
        // One set, as the search returns it: the whole set, with one fitting difficulty.
        Serve(Path(Query), null, Set(10, "Song", 2015, "ranked",
            Map(101, 3.2), Map(102, 4.8), Map(103, 6.1), Map(104, 5.0, length: 400), Map(105, 4.5, mode: "taiko")));

        var result = await Pool().DrawAsync(Query, NoSkip, default);

        Assert.Equal(MapPickResult.Found(new BeatmapInfo(102, 10, "Song", GameMode.Osu, 4.8, 120, "ranked")), result);
        Assert.Equal(
            "GET /api/v2/beatmapsets/search?q=stars%3E%3D4%20stars%3C%3D5.5%20length%3C%3D300%20ranked%3E%3D2012&m=0&s=ranked&sort=ranked_desc",
            _api.Requests.Last());
    }

    [Fact]
    public async Task Limits_left_at_zero_are_not_sent()
    {
        var open = new MapPickQuery(GameMode.Mania, 0, 0, 0, 0, 2015);
        Serve(Path(open), null, Set(10, "Keys", 2016, "ranked", Map(101, 2.0, mode: "mania")));

        var result = await Pool().DrawAsync(open, NoSkip, default);

        Assert.Equal(101, result.Beatmap!.Id);
        Assert.EndsWith("q=ranked%3E%3D2015&m=3&s=ranked&sort=ranked_desc", _api.Requests.Last(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hands_out_each_map_once_and_searches_once_for_the_whole_pool()
    {
        Serve(Path(Query), null, Set(10, "A", 2015, "ranked", Map(101, 4.1), Map(102, 4.2)), Set(11, "B", 2016, "ranked", Map(111, 4.3)));
        var pool = Pool();

        var ids = await DrawIdsAsync(pool, 3);

        Assert.Equal([101, 102, 111], ids.Order());
        Assert.Equal(1, _api.ApiRequests);
    }

    [Fact]
    public async Task Once_every_map_was_handed_out_it_searches_again_and_starts_over()
    {
        Serve(Path(Query), null, Set(10, "A", 2015, "ranked", Map(101, 4.1), Map(102, 4.2)));
        var pool = Pool();

        var ids = await DrawIdsAsync(pool, 3);

        Assert.Equal(2, ids.Take(2).Distinct().Count());
        Assert.Contains(ids[2], new[] { 101, 102 });
        Assert.Equal(2, _api.ApiRequests);
    }

    [Fact]
    public async Task Skipped_maps_are_never_drawn()
    {
        Serve(Path(Query), null, Set(10, "A", 2015, "ranked", Map(101, 4.1), Map(102, 4.2)));

        var ids = await DrawIdsAsync(Pool(), 2, skip: new HashSet<int> { 101 });

        Assert.Equal([102, 102], ids);
    }

    [Fact]
    public async Task New_limits_throw_the_pool_away()
    {
        var harder = Query with { StarMin = 6, StarMax = 7 };
        Serve(Path(Query), null, Set(10, "A", 2015, "ranked", Map(101, 4.1), Map(102, 4.2)));
        Serve(Path(harder), null, Set(20, "B", 2015, "ranked", Map(201, 6.5)));
        var pool = Pool();

        await pool.DrawAsync(Query, NoSkip, default);
        var result = await pool.DrawAsync(harder, NoSkip, default);

        Assert.Equal(201, result.Beatmap!.Id);
        Assert.Equal(2, _api.ApiRequests);
    }

    [Fact]
    public async Task Sets_that_are_not_ranked_or_too_old_and_converts_are_left_out()
    {
        Serve(Path(Query), null,
            Set(10, "Loved", 2015, "loved", Map(101, 4.1, status: "loved")),
            Set(11, "Old", 2010, "ranked", Map(111, 4.1)),
            Set(12, "Approved", 2013, "approved", Map(121, 4.1, status: "approved")),
            Set(13, "Fine", 2014, "ranked", Map(131, 4.1)));

        var ids = await DrawIdsAsync(Pool(), 2);

        Assert.Equal([121, 131], ids.Order());
    }

    [Fact]
    public async Task Nothing_fitting_is_none_fit()
    {
        Serve(Path(Query), null, Set(10, "Hard", 2015, "ranked", Map(101, 7.0)));

        Assert.Equal(new MapPickResult(MapPickOutcome.NoneFit), await Pool().DrawAsync(Query, NoSkip, default));
    }

    [Fact]
    public async Task An_empty_search_is_none_fit()
    {
        Serve(Path(Query), null);

        Assert.Equal(new MapPickResult(MapPickOutcome.NoneFit), await Pool().DrawAsync(Query, NoSkip, default));
    }

    [Fact]
    public async Task Only_skipped_maps_left_is_none_fit()
    {
        Serve(Path(Query), null, Set(10, "A", 2015, "ranked", Map(101, 4.1)));

        Assert.Equal(new MapPickResult(MapPickOutcome.NoneFit), await Pool().DrawAsync(Query, new HashSet<int> { 101 }, default));
    }

    [Fact]
    public async Task An_api_failure_is_failed()
    {
        _api.ThrowNetworkError = true;

        Assert.Equal(new MapPickResult(MapPickOutcome.Failed), await Pool().DrawAsync(Query, NoSkip, default));
    }

    [Fact]
    public async Task An_unreadable_answer_is_failed()
    {
        // A changed shape: no beatmapsets list, or a set whose id is not a number.
        _api.Routes[Path(Query)] = """{"results":[]}""";
        Assert.Equal(new MapPickResult(MapPickOutcome.Failed), await Pool().DrawAsync(Query, NoSkip, default));

        _api.Routes[Path(Query)] = """{"beatmapsets":[{"id":"x","title":"T","status":"ranked","ranked_date":"2015-01-01T00:00:00Z","beatmaps":[]}]}""";

        Assert.Equal(new MapPickResult(MapPickOutcome.Failed), await Pool().DrawAsync(Query, NoSkip, default));
    }

    [Fact]
    public async Task A_fill_takes_a_random_sort_and_skips_pages_along_the_cursor()
    {
        // Sort 2 (plays_desc), then skip 2 pages: only the third page's maps are kept.
        _random.Script.Enqueue(2);
        _random.Script.Enqueue(2);
        Serve(Path(Query, "plays_desc"), "c1", Set(10, "P1", 2015, "ranked", Map(101, 4.1)));
        Serve(Path(Query, "plays_desc", "c1"), "c2", Set(20, "P2", 2015, "ranked", Map(201, 4.1)));
        Serve(Path(Query, "plays_desc", "c2"), "c3", Set(30, "P3", 2015, "ranked", Map(301, 4.1), Map(302, 4.2)));

        var ids = await DrawIdsAsync(Pool(), 2);

        Assert.Equal([301, 302], ids.Order());
        Assert.Equal(3, _api.ApiRequests);
    }

    [Fact]
    public async Task When_the_results_end_before_the_skipped_pages_it_keeps_what_it_saw()
    {
        // Rules so narrow there is one page: skipping would leave nothing, so that page is used.
        _random.Script.Enqueue(0);
        _random.Script.Enqueue(3);
        Serve(Path(Query), null, Set(10, "Only", 2015, "ranked", Map(101, 4.1)));

        var result = await Pool().DrawAsync(Query, NoSkip, default);

        Assert.Equal(101, result.Beatmap!.Id);
        Assert.Equal(1, _api.ApiRequests);
    }

    [Fact]
    public async Task A_failure_after_the_first_page_keeps_what_it_saw()
    {
        _random.Script.Enqueue(0);
        _random.Script.Enqueue(1);
        Serve(Path(Query), "c1", Set(10, "First", 2015, "ranked", Map(101, 4.1)));

        // The cursor's page is not served, so it is a 404.
        var result = await Pool().DrawAsync(Query, NoSkip, default);

        Assert.Equal(101, result.Beatmap!.Id);
    }
}
