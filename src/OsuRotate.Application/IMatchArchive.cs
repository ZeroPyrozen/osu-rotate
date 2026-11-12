using OsuRotate.Domain;

namespace OsuRotate.Application;

// The match history and stats the dashboard shows (design figure 11: Match, MatchScore, Player).
// Unlike the lobby session, it is kept for good.
public interface IMatchArchive
{
    ValueTask SaveAsync(long? roomId, MatchRecord match, CancellationToken cancellationToken);

    // Newest first.
    ValueTask<IReadOnlyList<ArchivedMatch>> RecentAsync(int count, CancellationToken cancellationToken);
}

public sealed record ArchivedMatch(long Id, long? RoomId, MatchRecord Match);

public sealed class InMemoryMatchArchive : IMatchArchive
{
    private readonly List<ArchivedMatch> _matches = [];
    private readonly Lock _gate = new();

    public ValueTask SaveAsync(long? roomId, MatchRecord match, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _matches.Add(new ArchivedMatch(_matches.Count + 1, roomId, match));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<ArchivedMatch>> RecentAsync(int count, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyList<ArchivedMatch>>([.. Enumerable.Reverse(_matches).Take(count)]);
        }
    }
}
