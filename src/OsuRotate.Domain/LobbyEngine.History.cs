namespace OsuRotate.Domain;

public enum MatchOutcome
{
    Finished,
    Aborted,
}

public sealed record MatchRecordScore(string Username, int? OsuUserId, long Score, bool Passed);

// One match as the lobby saw it: who hosted, which map, when, how it ended and the scores Bancho
// reported. Players who never finished have no score.
public sealed record MatchRecord(
    int BeatmapId,
    string? HostName,
    int? HostUserId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    MatchOutcome Outcome,
    IReadOnlyList<MatchRecordScore> Scores)
{
    public bool Equals(MatchRecord? other) =>
        other is not null
        && BeatmapId == other.BeatmapId
        && HostName == other.HostName
        && HostUserId == other.HostUserId
        && StartedAt == other.StartedAt
        && EndedAt == other.EndedAt
        && Outcome == other.Outcome
        && Scores.SequenceEqual(other.Scores);

    public override int GetHashCode() => HashCode.Combine(BeatmapId, HostName, StartedAt, Outcome, Scores.Count);
}

// Collects a match's scores and reports it with RecordMatch when it ends, for the match history.
public sealed partial class LobbyEngine
{
    private readonly List<MatchRecordScore> _scores = [];
    private DateTimeOffset _matchStartedAt;
    private int _matchBeatmapId;
    private Player? _matchHost;

    private void HistoryOnMatchStarted()
    {
        _scores.Clear();
        _matchStartedAt = _time.GetUtcNow();
        _matchBeatmapId = Room.BeatmapId;
        _matchHost = Room.Host;
    }

    private void HistoryOnPlayerFinished(Player player, long score, bool passed)
    {
        _scores.RemoveAll(s => s.Username == player.Name);
        _scores.Add(new MatchRecordScore(player.Name, player.OsuUserId, score, passed));
    }

    private void HistoryOnMatchEnded(MatchOutcome outcome)
    {
        if (_matchStartedAt == default)
        {
            return;
        }

        _output.Add(new RecordMatch(new MatchRecord(
            _matchBeatmapId,
            _matchHost?.Name,
            _matchHost?.OsuUserId,
            _matchStartedAt,
            _time.GetUtcNow(),
            outcome,
            [.. _scores])));
        _scores.Clear();
        _matchStartedAt = default;
        _matchHost = null;
    }
}
