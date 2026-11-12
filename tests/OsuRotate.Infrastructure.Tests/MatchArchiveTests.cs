using Microsoft.Data.Sqlite;
using OsuRotate.Application;
using OsuRotate.Domain;
using OsuRotate.Infrastructure.Persistence;

namespace OsuRotate.Infrastructure.Tests;

public sealed class MatchArchiveTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly LobbySettings Settings = new() { OwnerName = "creator" };

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "osu-rotate-tests", Guid.NewGuid().ToString("N"));

    public MatchArchiveTests() => Directory.CreateDirectory(_folder);

    private string DatabasePath => Path.Combine(_folder, "rotate.db");

    private SqliteLobbyStore Open() => new(DatabasePath);

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

    private static MatchRecord Played(int beatmapId = 4242, string host = "p1", int minutes = 0, params MatchRecordScore[] scores) => new(
        beatmapId,
        host,
        111,
        T0.AddMinutes(minutes),
        T0.AddMinutes(minutes + 2),
        MatchOutcome.Finished,
        scores.Length == 0
            ? [new MatchRecordScore("p2", 222, 500_000, true), new MatchRecordScore("p1", 111, 812_345, true), new MatchRecordScore("guest", null, 0, false)]
            : scores);

    [Fact]
    public async Task A_played_match_round_trips_with_its_scores_in_order()
    {
        var store = Open();
        await store.SaveAsync(7, Played(), default);

        var match = Assert.Single(await Open().RecentAsync(10, default));
        Assert.Equal(7, match.RoomId);
        Assert.Equal(Played(), match.Match);
    }

    [Fact]
    public async Task An_aborted_match_with_no_host_or_scores_is_kept()
    {
        var aborted = new MatchRecord(1, null, null, T0, T0.AddSeconds(20), MatchOutcome.Aborted, []);
        await Open().SaveAsync(null, aborted, default);

        var match = Assert.Single(await Open().RecentAsync(10, default));
        Assert.Null(match.RoomId);
        Assert.Equal(aborted, match.Match);
    }

    [Fact]
    public async Task Recent_matches_come_newest_first_and_are_limited()
    {
        var store = Open();
        for (var i = 0; i < 5; i++)
        {
            await store.SaveAsync(7, Played(beatmapId: 100 + i, minutes: i), default);
        }

        var recent = await store.RecentAsync(3, default);
        Assert.Equal([104, 103, 102], recent.Select(m => m.Match.BeatmapId));
        Assert.True(recent[0].Id > recent[1].Id);
    }

    [Fact]
    public async Task A_match_is_linked_to_the_open_session_of_its_room()
    {
        var store = Open();
        await store.SaveAsync(new SavedLobby(new SavedSession(7, "room", [], null), [], Settings), default);
        await store.SaveAsync(7, Played(), default);
        await store.SaveAsync(8, Played(), default);

        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM Match WHERE SessionId IS NOT NULL"));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM Match m JOIN LobbySession s ON s.Id = m.SessionId WHERE s.RoomId = 7"));
    }

    [Fact]
    public async Task A_match_for_a_room_whose_session_closed_is_not_linked_to_it()
    {
        var store = Open();
        await store.SaveAsync(new SavedLobby(new SavedSession(7, "room", [], null), [], Settings), default);
        await store.SaveAsync(new SavedLobby(null, [], Settings), default);
        await store.SaveAsync(7, Played(), default);

        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM Match WHERE SessionId IS NOT NULL"));
        Assert.Equal(7, Assert.Single(await store.RecentAsync(10, default)).RoomId);
    }

    [Fact]
    public async Task Players_with_known_ids_are_remembered_with_their_latest_name()
    {
        var store = Open();
        await store.SaveAsync(7, Played(), default);
        await store.SaveAsync(7, Played(minutes: 10, scores: [new MatchRecordScore("p2 renamed", 222, 1, true)]), default);

        Assert.Equal(2, await CountAsync("SELECT COUNT(*) FROM Player"));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM Player WHERE OsuUserId = 222 AND Username = 'p2 renamed'"));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM Player WHERE OsuUserId = 222 AND FirstSeenAt < LastSeenAt"));
    }

    [Fact]
    public async Task The_host_is_remembered_even_without_a_score()
    {
        await Open().SaveAsync(7, Played(scores: [new MatchRecordScore("p2", 222, 1, true)]), default);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM Player WHERE OsuUserId = 111 AND Username = 'p1'"));
    }

    [Fact]
    public async Task Matches_survive_the_lobby_closing_and_the_process_going_away()
    {
        var store = Open();
        await store.SaveAsync(new SavedLobby(new SavedSession(7, "room", [], null), [], Settings), default);
        await store.SaveAsync(7, Played(), default);
        await store.SaveAsync(new SavedLobby(null, [], Settings), default);
        SqliteConnection.ClearAllPools();

        Assert.Single(await Open().RecentAsync(10, default));
    }

    [Fact]
    public async Task The_outcome_is_stored_by_name()
    {
        await Open().SaveAsync(7, Played() with { Outcome = MatchOutcome.Aborted }, default);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM Match WHERE Outcome = 'Aborted'"));
    }

    private async Task<long> CountAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
