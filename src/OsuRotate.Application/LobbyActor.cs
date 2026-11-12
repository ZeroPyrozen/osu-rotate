using System.Threading.Channels;
using OsuRotate.Domain;

namespace OsuRotate.Application;

// The single writer that owns the lobby engine. Bancho events, beatmap lookup answers and the owner's
// actions all go through one inbox and are handled one at a time, so the engine needs no locks and a
// recorded sequence of inputs always gives the same result. Between inputs the actor sleeps until the
// engine's next timer is due. The lifecycle decides whether there is a room and which events reach the
// engine; a new room starts the engine over with the same settings. What must survive a restart is
// saved after each input and before its commands leave (design figure 9).
public sealed class LobbyActor
{
    private readonly LobbyLifecycle _lifecycle;
    private readonly ILobbyStore? _store;
    private readonly IMatchArchive? _archive;
    private readonly List<(long? RoomId, MatchRecord Match)> _unsavedMatches = [];
    private SavedLobby _saved;
    private LobbyEngine _engine;
    private readonly ILobbyTransport _transport;
    private readonly IBeatmapCatalog _catalog;
    private readonly TimeProvider _time;
    private readonly ILobbyDiagnostics _diagnostics;
    private readonly Channel<Envelope> _inbox = Channel.CreateUnbounded<Envelope>(new UnboundedChannelOptions { SingleReader = true });
    private LobbySnapshot _snapshot;

    public LobbyActor(
        LobbyEngine engine,
        ILobbyTransport transport,
        IBeatmapCatalog catalog,
        TimeProvider time,
        ILobbyDiagnostics? diagnostics = null,
        LobbyLifecycle? lifecycle = null,
        ILobbyStore? store = null,
        IMatchArchive? archive = null)
    {
        _archive = archive;
        _engine = engine;
        _lifecycle = lifecycle ?? new LobbyLifecycle();
        _store = store;

        // The actor starts from what was loaded, so there is nothing new to save yet.
        _saved = SavedLobby.Of(engine, _lifecycle);
        _transport = transport;
        _catalog = catalog;
        _time = time;
        _diagnostics = diagnostics ?? NoLobbyDiagnostics.Instance;
        _snapshot = LobbySnapshot.Of(engine, _lifecycle);
    }

    public LobbySnapshot Snapshot => Volatile.Read(ref _snapshot);

    // Raised on the actor's loop after each input that changed the snapshot. Handlers must be quick.
    public event Action<LobbySnapshot>? SnapshotChanged;

    public ValueTask PostAsync(LobbyInput input, CancellationToken cancellationToken = default) =>
        _inbox.Writer.WriteAsync(new Envelope(input, null), cancellationToken);

    // Completes once every input posted before it has been handled.
    public Task DrainAsync(CancellationToken cancellationToken = default)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_inbox.Writer.TryWrite(new Envelope(null, done)))
        {
            done.SetCanceled(cancellationToken);
        }

        return done.Task.WaitAsync(cancellationToken);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var pump = PumpEventsAsync(cancellationToken);
        try
        {
            await LoopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _inbox.Writer.TryComplete();
            await pump.ConfigureAwait(false);
        }
    }

    private async Task PumpEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var lobbyEvent in _transport.Events(cancellationToken).ConfigureAwait(false))
            {
                await _inbox.Writer.WriteAsync(new Envelope(new BanchoEvent(lobbyEvent), null), cancellationToken).ConfigureAwait(false);
            }

            _diagnostics.EventsEnded(null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ChannelClosedException)
        {
        }
#pragma warning disable CA1031 // The transport failing must not take the actor down with it.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _diagnostics.EventsEnded(exception);
        }
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        // Kept across turns: a timer winning the race must not leave a second reader waiting.
        Task<bool>? waitForInput = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            while (_inbox.Reader.TryRead(out var envelope))
            {
                await HandleAsync(envelope, cancellationToken).ConfigureAwait(false);
            }

            if (_engine.NextDue is { } due && due <= _time.GetUtcNow())
            {
                await RunAsync(() => _engine.Tick(), null, cancellationToken).ConfigureAwait(false);
                continue;
            }

            waitForInput ??= _inbox.Reader.WaitToReadAsync(cancellationToken).AsTask();
            if (_engine.NextDue is { } next)
            {
                using var wake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var timer = Task.Delay(next - _time.GetUtcNow(), _time, wake.Token);
                await Task.WhenAny(waitForInput, timer).ConfigureAwait(false);
                await wake.CancelAsync().ConfigureAwait(false);
            }
            else
            {
                await Task.WhenAny(waitForInput).ConfigureAwait(false);
            }

            if (waitForInput.IsCompleted)
            {
                if (waitForInput.IsCanceled || !await waitForInput.ConfigureAwait(false))
                {
                    return;
                }

                waitForInput = null;
            }
        }
    }

    private async Task HandleAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        if (envelope.Drained is { } drained)
        {
            drained.TrySetResult();
            return;
        }

        var input = envelope.Input!;
        if (input is SayInLobby say)
        {
            await SendAsync(new Say(say.Text, CommandPriority.Admin), cancellationToken).ConfigureAwait(false);
            return;
        }

        await RunAsync(() => Apply(input), input, cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<LobbyCommand> Apply(LobbyInput input)
    {
        switch (input)
        {
            case BanchoEvent e:
                return OnBanchoEvent(e.Event);
            case CreateLobby or AttachLobby or CloseLobby or CloseLobbyWhenEmpty:
                var step = _lifecycle.OnInput(input, _engine.Settings);
                ResetIf(step);
                return step.Commands;
            case SkipHost:
                return _engine.Skip();
            case SkipToPlayer e:
                return _engine.SkipTo(e.Name);
            case ReorderQueue e:
                return _engine.Reorder(e.Names);
            case DenyPlayer e:
                return _engine.Deny(e.Name);
            case AllowPlayer e:
                return _engine.Allow(e.Name);
            case ChangeSettings e:
                _engine.UpdateSettings(e.Settings);
                return [];
            default:
                throw new ArgumentOutOfRangeException(nameof(input), input, "Unknown lobby input.");
        }
    }

    private List<LobbyCommand> OnBanchoEvent(LobbyEvent lobbyEvent)
    {
        var step = _lifecycle.OnEvent(lobbyEvent, _engine.Settings);
        ResetIf(step);
        var commands = new List<LobbyCommand>(step.Commands);
        if (step.Forward)
        {
            commands.AddRange(_engine.Handle(lobbyEvent));
            commands.AddRange(_lifecycle.AfterEngine(_engine.Room.Players.Count).Commands);
        }

        return commands;
    }

    private void ResetIf(LifecycleStep step)
    {
        if (step.ResetEngine)
        {
            _engine = new LobbyEngine(_engine.Settings, _time);
        }
    }

    private async Task RunAsync(Func<IReadOnlyList<LobbyCommand>> step, LobbyInput? input, CancellationToken cancellationToken)
    {
        IReadOnlyList<LobbyCommand> commands;
        try
        {
            commands = step();
        }
#pragma warning disable CA1031 // One bad input must not stop the lobby.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _diagnostics.InputFailed(input ?? new BanchoEvent(new TimerTick()), exception);
            commands = [];
        }

        foreach (var record in commands.OfType<RecordMatch>())
        {
            _unsavedMatches.Add((_lifecycle.RoomId, record.Match));
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
        await SaveMatchesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var command in commands)
        {
            if (command is RecordMatch)
            {
                continue;
            }

            if (command is LookUpBeatmap lookup)
            {
                _ = LookUpAsync(lookup, cancellationToken);
            }
            else
            {
                await SendAsync(command, cancellationToken).ConfigureAwait(false);
            }
        }

        Publish();
    }

    // If power fails between saving and sending, recovery reads the saved state and sends again.
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        // Until the saved queue is restored, the engine's queue is partial; saving it would lose the
        // saved one if the bot went down again before the room state arrived.
        if (_store is null || _engine.IsRestorePending)
        {
            return;
        }

        var lobby = SavedLobby.Of(_engine, _lifecycle);
        if (lobby.Equals(_saved))
        {
            return;
        }

        try
        {
            await _store.SaveAsync(lobby, cancellationToken).ConfigureAwait(false);
            _saved = lobby;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
#pragma warning disable CA1031 // A full disk must not stop the lobby; the next change saves again.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _diagnostics.SaveFailed(exception);
        }
    }

    // A match that could not be saved stays here and is tried again with the next input.
    private async Task SaveMatchesAsync(CancellationToken cancellationToken)
    {
        if (_archive is null)
        {
            _unsavedMatches.Clear();
            return;
        }

        while (_unsavedMatches.Count > 0)
        {
            var (roomId, match) = _unsavedMatches[0];
            try
            {
                await _archive.SaveAsync(roomId, match, cancellationToken).ConfigureAwait(false);
                _unsavedMatches.RemoveAt(0);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // The lobby goes on; the match is saved later.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                _diagnostics.SaveFailed(exception);
                return;
            }
        }
    }

    private async Task SendAsync(LobbyCommand command, CancellationToken cancellationToken)
    {
        try
        {
            await _transport.SendAsync(command, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
#pragma warning disable CA1031 // A command that cannot be sent is reported; the lobby goes on.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _diagnostics.SendFailed(command, exception);
        }
    }

    // Runs beside the loop. The answer comes back through the inbox like any other input.
    private async Task LookUpAsync(LookUpBeatmap lookup, CancellationToken cancellationToken)
    {
        BeatmapLookup result;
        try
        {
            result = await _catalog.FindAsync(lookup.BeatmapId, lookup.Mode, lookup.AllowConvert, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
#pragma warning disable CA1031 // The catalog should not throw, but if it does the map is let through.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _diagnostics.LookupFailed(lookup.BeatmapId, exception);
            result = new BeatmapLookup(BeatmapLookupOutcome.Failed, null);
        }

        _inbox.Writer.TryWrite(new Envelope(new BanchoEvent(new BeatmapLookedUp(lookup.BeatmapId, result)), null));
    }

    private void Publish()
    {
        var snapshot = LobbySnapshot.Of(_engine, _lifecycle);
        if (snapshot.Equals(_snapshot))
        {
            return;
        }

        Volatile.Write(ref _snapshot, snapshot);
        SnapshotChanged?.Invoke(snapshot);
    }

    private sealed record Envelope(LobbyInput? Input, TaskCompletionSource? Drained);

    // Stands in for "a timer fired" when reporting a failure from Tick.
    private sealed record TimerTick : LobbyEvent;
}
