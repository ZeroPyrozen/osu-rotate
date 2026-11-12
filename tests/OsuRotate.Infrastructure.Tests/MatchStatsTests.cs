using System.Diagnostics;
using Microsoft.Data.Sqlite;
using OsuRotate.Application;
using OsuRotate.Domain;
using OsuRotate.Infrastructure.Persistence;

namespace OsuRotate.Infrastructure.Tests;

// Phase 5, issue 07: pages of the match history and the stats (UC9), against a seeded database.
public sealed class MatchStatsTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "osu-rotate-tests", Guid.NewGuid().ToString("N"));

    public MatchStatsTests() => Directory.CreateDirectory(_folder);

    private string DatabasePath => Path.Combine(_folder, "rotate.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Left for the OS to clean up.
        }
    }

    private static MatchRecord Match(int beatmapId, DateTimeOffset startedAt, params MatchRecordScore[] scores) =>
        new(beatmapId, scores.FirstOrDefault()?.Username, scores.FirstOrDefault()?.OsuUserId, startedAt, startedAt.AddMinutes(3), MatchOutcome.Finished, scores);

    private static MatchRecordScore S(string name, int? id = null, long score = 1000) => new(name, id, score, true);

    // A small history with the awkward cases: a rename, guests with no id, an aborted match, and
    // matches either side of midnight in UTC and in UTC+7.
    private static readonly MatchRecord[] Seed =
    [
        Match(100, T0.AddDays(-40), S("old", 1)),
        Match(100, T0, S("alice", 1), S("bob", 2), S("guest")),
        Match(200, T0.AddHours(1), S("bob", 2), S("guest")),
        Match(100, T0.AddHours(11).AddMinutes(30), S("alice2", 1)),
        Match(300, T0.AddHours(12).AddMinutes(30), S("carol", 3), S("bob", 2)),
        new(200, null, null, T0.AddDays(1), T0.AddDays(1).AddSeconds(30), MatchOutcome.Aborted, []),
        Match(300, new DateTimeOffset(2026, 3, 2, 20, 0, 0, TimeSpan.FromHours(7)), S("Guest")),
    ];

    private async Task<(SqliteLobbyStore Store, InMemoryMatchArchive Reference)> SeedAsync()
    {
        var store = new SqliteLobbyStore(DatabasePath);
        var reference = new InMemoryMatchArchive();
        foreach (var match in Seed)
        {
            await store.SaveAsync(7, match, default);
            await reference.SaveAsync(7, match, default);
        }

        return (store, reference);
    }

    public static TheoryData<int, int> Windows => new()
    {
        { -60, 0 },
        { -30, 0 },
        { 0, 7 },
        { 0, -5 },
        { 1, 0 },
    };

    [Theory]
    [MemberData(nameof(Windows))]
    public async Task The_stats_agree_with_the_reference(int sinceDays, int offsetHours)
    {
        var (store, reference) = await SeedAsync();
        var query = new MatchStatsQuery(T0.AddDays(sinceDays), TimeSpan.FromHours(offsetHours));

        var expected = await reference.StatsAsync(query, default);
        var actual = await store.StatsAsync(query, default);
        Assert.Equal(expected.Matches, actual.Matches);
        Assert.Equal(expected.Maps, actual.Maps);
        Assert.Equal(expected.Players, actual.Players);
        Assert.Equal(expected.PerDay, actual.PerDay);
    }

    [Fact]
    public async Task The_stats_read_the_seeded_history()
    {
        var (store, _) = await SeedAsync();
        var stats = await store.StatsAsync(new MatchStatsQuery(T0.AddDays(-1), TimeSpan.FromHours(7)), default);

        Assert.Equal(6, stats.Matches);
        Assert.Equal([new MapPlays(100, 2), new MapPlays(200, 2), new MapPlays(300, 2)], stats.Maps);

        // alice renamed to alice2 and is one player; the two guests with no id are told apart by name.
        // Ties sort by name, ordinally.
        Assert.Equal(
            [new PlayerMatches("bob", 2, 3), new PlayerMatches("alice2", 1, 2), new PlayerMatches("guest", null, 2), new PlayerMatches("Guest", null, 1), new PlayerMatches("carol", 3, 1)],
            stats.Players);

        // In UTC+7 the matches at 23:30 and 00:30 UTC are both on 2 March.
        Assert.Equal(
            [new DayCount(new DateOnly(2026, 3, 1), 2), new DayCount(new DateOnly(2026, 3, 2), 4)],
            stats.PerDay);
    }

    [Fact]
    public async Task A_player_is_shown_by_the_name_they_last_hosted_under()
    {
        var (store, reference) = await SeedAsync();
        var renamed = new MatchRecord(300, "carol2", 3, T0.AddDays(2), T0.AddDays(2).AddMinutes(1), MatchOutcome.Aborted, []);
        await store.SaveAsync(7, renamed, default);
        await reference.SaveAsync(7, renamed, default);

        var query = new MatchStatsQuery(T0.AddDays(-1), TimeSpan.Zero);
        var players = (await store.StatsAsync(query, default)).Players;
        Assert.Contains(new PlayerMatches("carol2", 3, 1), players);
        Assert.Equal((await reference.StatsAsync(query, default)).Players, players);
    }

    [Fact]
    public async Task Top_limits_the_lists()
    {
        var (store, _) = await SeedAsync();
        var stats = await store.StatsAsync(new MatchStatsQuery(T0.AddDays(-60), TimeSpan.Zero, Top: 1), default);
        Assert.Equal([new MapPlays(100, 3)], stats.Maps);
        Assert.Single(stats.Players);
    }

    [Fact]
    public async Task No_matches_gives_empty_stats()
    {
        var stats = await new SqliteLobbyStore(DatabasePath).StatsAsync(new MatchStatsQuery(T0, TimeSpan.Zero), default);
        Assert.Equal(0, stats.Matches);
        Assert.Empty(stats.Maps);
        Assert.Empty(stats.Players);
        Assert.Empty(stats.PerDay);
    }

    [Fact]
    public async Task Times_are_stored_in_utc_and_read_back_as_the_same_instant()
    {
        var store = new SqliteLobbyStore(DatabasePath);
        var local = new DateTimeOffset(2026, 3, 2, 20, 0, 0, TimeSpan.FromHours(7));
        await store.SaveAsync(null, Match(1, local), default);

        var read = Assert.Single((await store.PageAsync(null, 10, default)).Matches).Match;
        Assert.Equal(local, read.StartedAt);
        Assert.Equal(TimeSpan.Zero, read.StartedAt.Offset);
    }

    [Fact]
    public async Task Pages_go_back_from_the_newest_and_agree_with_the_reference()
    {
        var (store, reference) = await SeedAsync();

        var first = await store.PageAsync(null, 3, default);
        Assert.Equal([7L, 6, 5], first.Matches.Select(m => m.Id));
        Assert.True(first.HasOlder);
        Assert.Equal(5, first.OlderThan);

        var last = await store.PageAsync(first.OlderThan, 4, default);
        Assert.Equal([4L, 3, 2, 1], last.Matches.Select(m => m.Id));
        Assert.False(last.HasOlder);
        Assert.Null(last.OlderThan);

        var expected = await reference.PageAsync(5, 2, default);
        var actual = await store.PageAsync(5, 2, default);
        Assert.Equal(expected.Matches.Select(m => (m.Id, m.Match)), actual.Matches.Select(m => (m.Id, m.Match)));
        Assert.Equal(expected.HasOlder, actual.HasOlder);
    }

    [Fact]
    public async Task An_empty_history_has_an_empty_page()
    {
        var page = await new SqliteLobbyStore(DatabasePath).PageAsync(null, 20, default);
        Assert.Empty(page.Matches);
        Assert.False(page.HasOlder);
    }

    // A year of a busy lobby: a match every 4 minutes, around the clock, with 8 scores each.
    [Fact]
    public async Task A_year_of_matches_stays_fast()
    {
        var store = new SqliteLobbyStore(DatabasePath);
        await store.PageAsync(null, 1, default);
        const int matches = 365 * 24 * 15;
        var end = T0;
        await using (var connection = new SqliteConnection($"Data Source={DatabasePath}"))
        {
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();
            var match = connection.CreateCommand();
            match.CommandText = "INSERT INTO \"Match\" (Id, RoomId, BeatmapId, HostName, HostUserId, StartedAt, EndedAt, Outcome) VALUES ($id, 7, $map, $host, $hostId, $start, $end, 'Finished')";
            var score = connection.CreateCommand();
            score.CommandText = "INSERT INTO \"MatchScore\" (MatchId, Position, Username, OsuUserId, Score, Passed) VALUES ($match, $pos, $name, $user, 100000, 1)";
            foreach (var name in new[] { "$id", "$map", "$host", "$hostId", "$start", "$end" })
            {
                match.Parameters.Add(new SqliteParameter { ParameterName = name });
            }

            foreach (var name in new[] { "$match", "$pos", "$name", "$user" })
            {
                score.Parameters.Add(new SqliteParameter { ParameterName = name });
            }

            for (var i = 1; i <= matches; i++)
            {
                var started = end.AddMinutes(-4 * (matches - i));
                var host = i % 500;
                match.Parameters["$id"].Value = i;
                match.Parameters["$map"].Value = i % 2000;
                match.Parameters["$host"].Value = $"p{host}";
                match.Parameters["$hostId"].Value = host;
                match.Parameters["$start"].Value = started.ToString(@"yyyy\-MM\-dd HH\:mm\:ss.FFFFFFFzzz", System.Globalization.CultureInfo.InvariantCulture);
                match.Parameters["$end"].Value = started.AddMinutes(3).ToString(@"yyyy\-MM\-dd HH\:mm\:ss.FFFFFFFzzz", System.Globalization.CultureInfo.InvariantCulture);
                await match.ExecuteNonQueryAsync();
                for (var p = 0; p < 8; p++)
                {
                    var player = (i + (p * 61)) % 500;
                    score.Parameters["$match"].Value = i;
                    score.Parameters["$pos"].Value = p;
                    score.Parameters["$name"].Value = $"p{player}";
                    score.Parameters["$user"].Value = player;
                    await score.ExecuteNonQueryAsync();
                }
            }

            await transaction.CommitAsync();
        }

        var clock = Stopwatch.StartNew();
        var page = await store.PageAsync(null, 25, default);
        var older = await store.PageAsync(matches / 2, 25, default);
        var pageTime = clock.Elapsed;

        clock.Restart();
        var month = await store.StatsAsync(new MatchStatsQuery(end.AddDays(-30), TimeSpan.Zero), default);
        var monthTime = clock.Elapsed;

        clock.Restart();
        var year = await store.StatsAsync(new MatchStatsQuery(end.AddDays(-366), TimeSpan.Zero), default);
        var yearTime = clock.Elapsed;

        TestContext.Current.TestOutputHelper?.WriteLine($"pages {pageTime.TotalMilliseconds:0} ms, month {monthTime.TotalMilliseconds:0} ms, year {yearTime.TotalMilliseconds:0} ms");
        Assert.Equal(matches, page.Matches[0].Id);
        Assert.Equal(matches / 2 - 1, older.Matches[0].Id);
        Assert.Equal(30 * 24 * 15 + 1, month.Matches);
        Assert.Equal(matches, year.Matches);
        Assert.Equal(10, year.Players.Count);
        Assert.InRange(year.PerDay.Count, 365, 367);

        // Generous bounds for a slow machine or a Raspberry Pi; locally these take a few hundred ms.
        Assert.True(pageTime < TimeSpan.FromSeconds(1), $"pages took {pageTime}");
        Assert.True(monthTime < TimeSpan.FromSeconds(2), $"a month of stats took {monthTime}");
        Assert.True(yearTime < TimeSpan.FromSeconds(10), $"a year of stats took {yearTime}");
    }
}
