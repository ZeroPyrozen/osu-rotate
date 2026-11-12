namespace OsuRotate.Domain;

// A match found running in the room after a restart: when it began and on which map.
public sealed record RestoredMatch(DateTimeOffset StartedAt, int BeatmapId);

// Recovery after a restart. The saved queue is applied when the first !mp settings after rejoining
// arrives, instead of rebuilding the queue in slot order, because during a match the queue has
// already rotated: its front is the next host, not the current one.
public sealed partial class LobbyEngine
{
    private (IReadOnlyList<string> Queue, string? Host, RestoredMatch? Match)? _restore;

    public bool IsRestorePending => _restore is not null;

    // queue and host are what was saved; runningMatch is set when the match API shows a game in
    // progress. Players no longer in the room are dropped; newcomers follow in slot order.
    public void RestoreOnNextRoomState(IReadOnlyList<string> queue, string? host, RestoredMatch? runningMatch) =>
        _restore = (queue, host, runningMatch);

    // The match ended before the room state came back, so there is no match to resume.
    private void RecoveryOnMatchEnded()
    {
        if (_restore is { } plan)
        {
            _restore = plan with { Match = null };
        }
    }

    private bool TryRestore(RoomState state)
    {
        if (_restore is not { } plan)
        {
            return false;
        }

        _restore = null;
        var order = plan.Queue.Select(Room.Find).OfType<Player>().ToList();
        if (plan.Match is { } match)
        {
            // Host stays with whoever has it until the match finishes; then the front of the queue,
            // the next host, gets it as usual.
            Room.StartMatch();
            Rotation.Restore(order, state, changeHost: false);
            _matchStartedAt = match.StartedAt;
            _matchBeatmapId = match.BeatmapId;
            _matchHost = Room.Host ?? (plan.Host is { } name ? Room.Find(name) : null);
            _scores.Clear();
            AbortVotes.SetVoters(Room.Players);
        }
        else
        {
            Rotation.Restore(order, state, changeHost: true);
        }

        return true;
    }
}
