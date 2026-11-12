namespace OsuRotate.Application;

// A multiplayer room's history from the osu! API: who joined, left, was kicked or got host, and the
// games played. Recovery reads it to see whether a match is running; the host-kick counterattack reads
// the kicks, which Bancho's chat never shows.
public interface IMatchHistory
{
    // Events after afterEventId, or the latest page when it is null. Errors return Failed, never throw.
    ValueTask<MatchLookup> GetAsync(long matchId, long? afterEventId, CancellationToken cancellationToken);
}

public enum MatchLookupOutcome
{
    Found,
    NotFound,
    Failed,
}

public sealed record MatchLookup(MatchLookupOutcome Outcome, MatchHistory? History)
{
    public static MatchLookup Found(MatchHistory history) => new(MatchLookupOutcome.Found, history);

    public static MatchLookup NotFound { get; } = new(MatchLookupOutcome.NotFound, null);

    public static MatchLookup Failed { get; } = new(MatchLookupOutcome.Failed, null);
}

public sealed record MatchHistory(
    long MatchId,
    string Name,
    DateTimeOffset StartTime,
    DateTimeOffset? EndTime,
    long? CurrentGameId,
    long LatestEventId,
    IReadOnlyList<MatchEvent> Events,
    IReadOnlyDictionary<int, string> Users)
{
    // current_game_id stays set for a moment after a game ends (seen live), so the game must also have
    // no end time to count as running.
    public MatchGame? RunningGame =>
        CurrentGameId is { } id && Events.Select(e => e.Game).FirstOrDefault(g => g?.Id == id) is { EndTime: null } game ? game : null;

    public string? UsernameOf(int? userId) => userId is { } id && Users.TryGetValue(id, out var name) ? name : null;
}

// Type is the API's detail.type: match-created, match-disbanded, host-changed, player-joined,
// player-left, player-kicked or other (a game).
public sealed record MatchEvent(long Id, string Type, DateTimeOffset Timestamp, int? UserId, MatchGame? Game);

public sealed record MatchGame(long Id, DateTimeOffset StartTime, DateTimeOffset? EndTime, int BeatmapId, IReadOnlyList<MatchGameScore> Scores);

public sealed record MatchGameScore(int UserId, long Score, bool Passed);
