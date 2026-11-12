using OsuRotate.Domain;

namespace OsuRotate.Application;

public sealed record RecoveredLobby(LobbyEngine Engine, LobbyLifecycle Lifecycle, SavedSession? Session, bool MatchRunning);

// Starting up (design figure 4): load what was saved, rebuild the engine with the owner's rules and
// deny list, and if a room was open, ask the match API whether a game is running in it. The lifecycle
// then rejoins the room, and the first !mp settings restores the saved queue.
public static class LobbyRecovery
{
    // configured carries what comes from the host, not the database: the owner's account and the
    // running version.
    public static async ValueTask<RecoveredLobby> RecoverAsync(
        ILobbyStore store,
        IMatchHistory history,
        LobbySettings configured,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(configured);

        var saved = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var settings = saved.Settings is { } rules
            ? rules with { OwnerName = configured.OwnerName, BotVersion = configured.BotVersion }
            : configured;

        var engine = new LobbyEngine(settings, time);
        foreach (var denied in saved.Denied)
        {
            engine.Deny(denied);
        }

        if (saved.Session is not { } session)
        {
            return new RecoveredLobby(engine, new LobbyLifecycle(), null, MatchRunning: false);
        }

        var running = await RunningMatchAsync(history, session.RoomId, cancellationToken).ConfigureAwait(false);
        engine.RestoreOnNextRoomState(session.Queue, session.Host, running);
        return new RecoveredLobby(engine, new LobbyLifecycle(session.RoomId, session.Title), session, running is not null);
    }

    // No answer (no API key, or the API is down) counts as no match. Getting that wrong only hands host
    // to the next player a little early; guessing a match that has ended would leave the lobby waiting.
    private static async ValueTask<RestoredMatch?> RunningMatchAsync(IMatchHistory history, long roomId, CancellationToken cancellationToken)
    {
        MatchLookup lookup;
        try
        {
            lookup = await history.GetAsync(roomId, afterEventId: null, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // The history should not throw; if it does, start as if no match runs.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }

        return lookup is { Outcome: MatchLookupOutcome.Found, History.RunningGame: { } game }
            ? new RestoredMatch(game.StartTime, game.BeatmapId)
            : null;
    }
}

// For running without an osu! API key: no history, so recovery assumes no match is running.
public sealed class NoMatchHistory : IMatchHistory
{
    public static NoMatchHistory Instance { get; } = new();

    public ValueTask<MatchLookup> GetAsync(long matchId, long? afterEventId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(MatchLookup.Failed);
}
