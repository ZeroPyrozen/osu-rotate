using System.Collections.Concurrent;
using System.Threading.Channels;
using OsuRotate.Application;
using OsuRotate.Domain;

namespace OsuRotate.Application.Tests;

internal sealed class FakeTransport : ILobbyTransport
{
    private readonly Channel<LobbyEvent> _events = Channel.CreateUnbounded<LobbyEvent>();
    private readonly ConcurrentQueue<LobbyCommand> _sent = new();
    private readonly SemaphoreSlim _changed = new(0);

    public TransportCapabilities Capabilities { get; } = new(CanSetHost: true, HasChat: true);

    public IReadOnlyList<LobbyCommand> Sent => [.. _sent];

    // Commands of this type throw instead of being sent.
    public Type? Failing { get; set; }

    public void Emit(LobbyEvent lobbyEvent) => _events.Writer.TryWrite(lobbyEvent);

    public void ClearSent() => _sent.Clear();

    public void End(Exception? error = null) => _events.Writer.TryComplete(error);

    public IAsyncEnumerable<LobbyEvent> Events(CancellationToken cancellationToken) => _events.Reader.ReadAllAsync(cancellationToken);

    public ValueTask SendAsync(LobbyCommand command, CancellationToken cancellationToken)
    {
        if (command.GetType() == Failing)
        {
            throw new InvalidOperationException("send failed");
        }

        _sent.Enqueue(command);
        _changed.Release();
        return ValueTask.CompletedTask;
    }

    public async Task<IReadOnlyList<LobbyCommand>> WaitForAsync(Func<IReadOnlyList<LobbyCommand>, bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition(Sent))
        {
            await _changed.WaitAsync(timeout.Token);
        }

        return Sent;
    }
}

internal sealed class FakeCatalog : IBeatmapCatalog
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource<BeatmapLookup>> _pending = new();

    public ConcurrentQueue<int> Asked { get; } = new();

    // While true, lookups wait for Answer.
    public bool Hold { get; set; }

    public bool Throw { get; set; }

    public static BeatmapInfo Map(int id) => new(id, id * 10, $"title {id}", GameMode.Osu, 5, 120, "ranked");

    public ValueTask<BeatmapLookup> FindAsync(int beatmapId, GameMode mode, bool allowConvert, CancellationToken cancellationToken)
    {
        Asked.Enqueue(beatmapId);
        if (Throw)
        {
            throw new HttpRequestException("api down");
        }

        if (!Hold)
        {
            return ValueTask.FromResult(BeatmapLookup.Found(Map(beatmapId)));
        }

        var pending = _pending.GetOrAdd(beatmapId, _ => new TaskCompletionSource<BeatmapLookup>(TaskCreationOptions.RunContinuationsAsynchronously));
        return new ValueTask<BeatmapLookup>(pending.Task.WaitAsync(cancellationToken));
    }

    public void Answer(int beatmapId) =>
        _pending.GetOrAdd(beatmapId, _ => new TaskCompletionSource<BeatmapLookup>()).TrySetResult(BeatmapLookup.Found(Map(beatmapId)));
}

// Records each save with how many commands the transport had sent at that moment.
internal sealed class RecordingStore(FakeTransport transport) : ILobbyStore
{
    public ConcurrentQueue<(SavedLobby Lobby, int SentBefore)> Saves { get; } = new();

    public bool Fail { get; set; }

    public SavedLobby? Last => Saves.LastOrDefault().Lobby;

    public ValueTask<SavedLobby> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(SavedLobby.Empty);

    public ValueTask SaveAsync(SavedLobby lobby, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            throw new IOException("disk full");
        }

        Saves.Enqueue((lobby, transport.Sent.Count));
        return ValueTask.CompletedTask;
    }
}

internal sealed class RecordingDiagnostics : ILobbyDiagnostics
{
    public ConcurrentQueue<string> Reports { get; } = new();

    public void InputFailed(LobbyInput input, Exception exception) => Reports.Enqueue($"input {input.GetType().Name}: {exception.GetType().Name}");

    public void SendFailed(LobbyCommand command, Exception exception) => Reports.Enqueue($"send {command.GetType().Name}");

    public void LookupFailed(int beatmapId, Exception exception) => Reports.Enqueue($"lookup {beatmapId}");

    public void EventsEnded(Exception? exception) => Reports.Enqueue($"ended {exception?.Message ?? "cleanly"}");

    public void SaveFailed(Exception exception) => Reports.Enqueue($"save {exception.Message}");
}
