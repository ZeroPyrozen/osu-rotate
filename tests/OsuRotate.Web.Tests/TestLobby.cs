using System.Collections.Concurrent;
using System.Threading.Channels;
using OsuRotate.Application;
using OsuRotate.Domain;
using OsuRotate.Web.Bot;

namespace OsuRotate.Web.Tests;

// A real lobby actor on a scripted transport, attached to a LobbyRuntime, for testing dashboard
// controls without Bancho or a browser.
public sealed class TestLobby : IAsyncDisposable
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource _stop = new();
    private readonly Task _run;

    public TestLobby(Func<LobbySettings, LobbySettings>? configure = null, LobbyLifecycle? lifecycle = null)
    {
        var settings = new LobbySettings { OwnerName = "creator", HostAfkCheckInterval = TimeSpan.Zero };
        var engine = new LobbyEngine(configure?.Invoke(settings) ?? settings, TimeProvider.System);
        Actor = new LobbyActor(engine, Transport, new NoCatalog(), TimeProvider.System, lifecycle: lifecycle);
        _run = Actor.RunAsync(_stop.Token);
        Runtime.Attach(Actor);
    }

    public LobbyRuntime Runtime { get; } = new();

    public ScriptedTransport Transport { get; } = new();

    public LobbyActor Actor { get; }

    public LobbySnapshot Snapshot => Actor.Snapshot;

    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    public async Task ConnectAsync()
    {
        Transport.Emit(new TransportConnected());
        await WaitUntilAsync(() => Snapshot.Connected);
    }

    // Connected and in room 7 with the given players; the first is host.
    public async Task OpenAsync(params string[] players)
    {
        await ConnectAsync();
        await Actor.PostAsync(new AttachLobby(7));
        await WaitUntilAsync(() => Snapshot.Status == LobbyStatus.Rejoining);
        Transport.Emit(new RoomJoined(7));
        Transport.Emit(new RoomStateReceived(new RoomState("room", 7, 0, string.Empty, "HeadToHead", "Score", string.Empty,
            [.. players.Select((name, i) => new RoomPlayer(i + 1, "Not Ready", (i + 1) * 11, name, i == 0, Team.None, i == 0 ? "Host" : string.Empty))])));
        await WaitUntilAsync(() => Snapshot.Status == LobbyStatus.Open && Snapshot.Players.Count == players.Length);
        Transport.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _run;
        _stop.Dispose();
    }

    public sealed class ScriptedTransport : ILobbyTransport
    {
        private readonly Channel<LobbyEvent> _events = Channel.CreateUnbounded<LobbyEvent>();
        private readonly ConcurrentQueue<LobbyCommand> _sent = new();

        public IReadOnlyList<LobbyCommand> Sent => [.. _sent];

        public TransportCapabilities Capabilities { get; } = new(CanSetHost: true, HasChat: true);

        public void Emit(LobbyEvent lobbyEvent) => _events.Writer.TryWrite(lobbyEvent);

        public void Clear() => _sent.Clear();

        public IAsyncEnumerable<LobbyEvent> Events(CancellationToken cancellationToken) => _events.Reader.ReadAllAsync(cancellationToken);

        public ValueTask SendAsync(LobbyCommand command, CancellationToken cancellationToken)
        {
            _sent.Enqueue(command);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoCatalog : IBeatmapCatalog
    {
        public ValueTask<BeatmapLookup> FindAsync(int beatmapId, GameMode mode, bool allowConvert, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new BeatmapLookup(BeatmapLookupOutcome.Failed, null));
    }
}
