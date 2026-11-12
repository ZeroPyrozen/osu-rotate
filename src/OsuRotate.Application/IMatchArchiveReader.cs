using OsuRotate.Domain;

namespace OsuRotate.Application;

// What the dashboard reads from the match history (UC9): pages of recent matches and simple stats.
public interface IMatchArchiveReader
{
    // Newest first, older than beforeId when given. A page knows whether older matches remain.
    ValueTask<MatchPage> PageAsync(long? beforeId, int size, CancellationToken cancellationToken);

    // Over the matches started since then. Days are counted in the given time zone offset.
    ValueTask<MatchStats> StatsAsync(MatchStatsQuery query, CancellationToken cancellationToken);
}

public sealed record MatchPage(IReadOnlyList<ArchivedMatch> Matches, bool HasOlder)
{
    public static MatchPage Empty { get; } = new([], false);

    // Where the next (older) page starts.
    public long? OlderThan => HasOlder && Matches.Count > 0 ? Matches[^1].Id : null;
}

public sealed record MatchStatsQuery(DateTimeOffset Since, TimeSpan DayOffset, int Top = 10)
{
    public const int MaxTop = 100;
}

public sealed record MapPlays(int BeatmapId, int Plays);

// Players are told apart by osu! user id when it is known, shown by the name they last used. A
// player has one score per match, so Matches counts their scores.
public sealed record PlayerMatches(string Name, int? OsuUserId, int Matches);

public sealed record DayCount(DateOnly Day, int Matches);

public sealed record MatchStats(int Matches, IReadOnlyList<MapPlays> Maps, IReadOnlyList<PlayerMatches> Players, IReadOnlyList<DayCount> PerDay)
{
    public static MatchStats Empty { get; } = new(0, [], [], []);
}

// The same answers from a list of matches, for tests and for running without a database. The SQLite
// store must agree with it.
public static class MatchQueries
{
    public static MatchPage Page(IEnumerable<ArchivedMatch> matches, long? beforeId, int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        var older = matches.Where(m => beforeId is not { } before || m.Id < before).OrderByDescending(m => m.Id).Take(size + 1).ToList();
        return new MatchPage([.. older.Take(size)], older.Count > size);
    }

    public static MatchStats Stats(IEnumerable<ArchivedMatch> matches, MatchStatsQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var top = Math.Clamp(query.Top, 1, MatchStatsQuery.MaxTop);
        var since = matches.Where(m => m.Match.StartedAt >= query.Since).ToList();

        var maps = since.GroupBy(m => m.Match.BeatmapId)
            .Select(g => new MapPlays(g.Key, g.Count()))
            .OrderByDescending(m => m.Plays).ThenBy(m => m.BeatmapId)
            .Take(top);

        // A player with a known id is shown by the name last seen anywhere in the history, as a host
        // or with a score.
        var names = new Dictionary<int, string>();
        foreach (var match in matches.OrderBy(m => m.Id))
        {
            foreach (var score in match.Match.Scores.Where(s => s.OsuUserId is not null))
            {
                names[score.OsuUserId!.Value] = score.Username;
            }

            if (match.Match is { HostUserId: { } hostId, HostName: { } hostName })
            {
                names[hostId] = hostName;
            }
        }

        var scores = since.SelectMany(m => m.Match.Scores.Select(s => (m.Id, s))).ToList();
        var players = scores.GroupBy(x => x.s.OsuUserId is { } id ? $"id:{id}" : $"name:{x.s.Username}", StringComparer.Ordinal)
            .Select(g => g.First().s.OsuUserId is { } id
                ? new PlayerMatches(names[id], id, g.Count())
                : new PlayerMatches(g.First().s.Username, null, g.Count()))
            .OrderByDescending(p => p.Matches).ThenBy(p => p.Name, StringComparer.Ordinal)
            .Take(top);

        var perDay = since.GroupBy(m => DateOnly.FromDateTime(m.Match.StartedAt.ToOffset(query.DayOffset).DateTime))
            .Select(g => new DayCount(g.Key, g.Count()))
            .OrderBy(d => d.Day);

        return new MatchStats(since.Count, [.. maps], [.. players], [.. perDay]);
    }
}
