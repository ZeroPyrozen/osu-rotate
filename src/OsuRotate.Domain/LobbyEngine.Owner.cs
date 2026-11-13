namespace OsuRotate.Domain;

// The owner forcing the match from the dashboard, regardless of votes: the same as *start, *abort and
// !stop in chat.
public sealed partial class LobbyEngine
{
    public const int MaxCountdownSeconds = 300;

    // Zero starts at once; otherwise a countdown with the usual 30 and 10 second warnings.
    public IReadOnlyList<LobbyCommand> ForceStart(int countdownSeconds)
    {
        if (!Room.IsMatching && countdownSeconds is >= 0 and <= MaxCountdownSeconds)
        {
            _ownerStartOverride = true;
            StartMatchIn(countdownSeconds);
        }

        return Flush();
    }

    public IReadOnlyList<LobbyCommand> ForceAbort()
    {
        if (Room.IsMatching)
        {
            AbortMatchNow();
        }

        return Flush();
    }

    public IReadOnlyList<LobbyCommand> CancelCountdown()
    {
        if (!Room.IsMatching && IsStartTimerRunning)
        {
            Announce("Aborted the match start timer.");
            StopStartTimer();
        }

        return Flush();
    }
}
