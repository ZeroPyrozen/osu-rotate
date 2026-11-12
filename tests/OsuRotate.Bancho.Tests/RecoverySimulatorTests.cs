using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using OsuRotate.Application;
using OsuRotate.Domain;
using OsuRotate.Infrastructure.Bancho;
using OsuRotate.Infrastructure.Persistence;

namespace OsuRotate.Bancho.Tests;

// Phase 4's done criterion: kill the process mid-match and the same queue comes back. Each "process"
// is a transport, a lobby actor and a SQLite store on the same file; killing one cancels it without
// any shutdown work, as a crash or power loss would.
public sealed class RecoverySimulatorTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly LobbySettings Configured = new() { OwnerName = "creator", HostAfkCheckInterval = TimeSpan.Zero };

    private readonly BanchoSimulator _server = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "osu-rotate-tests", Guid.NewGuid().ToString("N"));
    private readonly List<Process> _processes = [];

    private string DatabasePath => Path.Combine(_folder, "rotate.db");

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_folder);
        _server.Start();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var process in _processes)
        {
            await process.KillAsync();
        }

        await _server.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Left for the OS to clean up.
        }
    }

    private async Task<Process> StartAsync(IMatchHistory history)
    {
        var store = new SqliteLobbyStore(DatabasePath);
        var recovered = await LobbyRecovery.RecoverAsync(store, history, Configured, TimeProvider.System, default);
        var transport = new BanchoIrcTransport(
            new BanchoOptions
            {
                Host = "127.0.0.1",
                Port = _server.Port,
                Nick = "creator",
                Password = "secret",
                ReconnectBaseDelay = TimeSpan.FromMilliseconds(20),
                ReconnectMaxDelay = TimeSpan.FromMilliseconds(50),
            },
            NullLogger<BanchoIrcTransport>.Instance);
        var actor = new LobbyActor(recovered.Engine, transport, new NoCatalog(), TimeProvider.System, lifecycle: recovered.Lifecycle, store: store, archive: store);
        var process = new Process(actor, transport, recovered);
        _processes.Add(process);
        return process;
    }

    private async Task WaitForLine(string line, int count = 1)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        while (_server.Received.Count(r => r.Line == line) < count)
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task<LobbySnapshot> WaitFor(Process process, Func<LobbySnapshot, bool> condition)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        while (!condition(process.Actor.Snapshot))
        {
            await Task.Delay(10, timeout.Token);
        }

        return process.Actor.Snapshot;
    }

    // Three players, host given to p1, a map picked and a match started: the queue has rotated.
    private async Task<long> PlayUntilMidMatchAsync(Process first)
    {
        await first.Actor.PostAsync(new CreateLobby("my room"));
        var open = await WaitFor(first, s => s.Status == LobbyStatus.Open);
        var channel = $"#mp_{open.RoomId}";
        _server.AddPlayer("p1", 11);
        _server.AddPlayer("p2", 22);
        _server.AddPlayer("p3", 33);
        await WaitFor(first, s => s.Host == "p1" && s.Queue.Count == 3);

        _server.FromBot(channel, "Beatmap changed to: artist - title [diff] (https://osu.ppy.sh/b/4242)");
        _server.FromBot(channel, "The match has started!");
        var midMatch = await WaitFor(first, s => s.IsMatching);
        Assert.Equal(["p2", "p3", "p1"], midMatch.Queue);
        Assert.Equal("p1", midMatch.Host);
        await first.Actor.DrainAsync().WaitAsync(Timeout);
        return open.RoomId!.Value;
    }

    [Fact]
    public async Task Killing_the_process_mid_match_restores_the_same_queue()
    {
        var first = await StartAsync(NoMatchHistory.Instance);
        var roomId = await PlayUntilMidMatchAsync(first);
        await first.KillAsync();

        // The match is still running when the bot comes back; the match API says so.
        var second = await StartAsync(new RunningGameHistory(roomId));
        Assert.True(second.Recovered.MatchRunning);
        await WaitForLine($"JOIN #mp_{roomId}");
        var restored = await WaitFor(second, s => s.Status == LobbyStatus.Open && s.Queue.Count == 3);

        Assert.Equal(roomId, restored.RoomId);
        Assert.Equal(["p2", "p3", "p1"], restored.Queue);
        Assert.True(restored.IsMatching);
        Assert.Equal(1, _server.Received.Count(r => r.Line == "PRIVMSG BanchoBot :!mp make my room"));
        Assert.DoesNotContain(_server.Received, r => r.Line == $"PRIVMSG #mp_{roomId} :!mp host #22");

        // When the match finishes, the next player in the saved order hosts.
        _server.FromBot($"#mp_{roomId}", "p1 finished playing (Score: 812345, PASSED).");
        _server.FromBot($"#mp_{roomId}", "The match has finished!");
        await WaitForLine($"PRIVMSG #mp_{roomId} :!mp host #22");
        var after = await WaitFor(second, s => s.Host == "p2");
        Assert.Equal(["p2", "p3", "p1"], after.Queue);

        var match = Assert.Single(await new SqliteLobbyStore(DatabasePath).RecentAsync(10, default));
        Assert.Equal(4242, match.Match.BeatmapId);
        Assert.Equal("p1", match.Match.HostName);
        Assert.Equal(RunningGameHistory.Started, match.Match.StartedAt);
    }

    [Fact]
    public async Task A_match_that_finished_while_the_bot_was_down_passes_host_on_at_once()
    {
        var first = await StartAsync(NoMatchHistory.Instance);
        var roomId = await PlayUntilMidMatchAsync(first);
        await first.KillAsync();

        // No API key, so no way to tell; recovery assumes the match is over.
        var second = await StartAsync(NoMatchHistory.Instance);
        Assert.False(second.Recovered.MatchRunning);
        await WaitForLine($"PRIVMSG #mp_{roomId} :!mp host #22");
        var restored = await WaitFor(second, s => s.Host == "p2");

        Assert.Equal(["p2", "p3", "p1"], restored.Queue);
        Assert.False(restored.IsMatching);
    }

    [Fact]
    public async Task Players_who_came_and_went_while_the_bot_was_down_are_merged_in()
    {
        var first = await StartAsync(NoMatchHistory.Instance);
        var roomId = await PlayUntilMidMatchAsync(first);
        await first.KillAsync();
        _server.RemovePlayer("p3");
        _server.AddPlayer("p4", 44);

        var second = await StartAsync(NoMatchHistory.Instance);
        var restored = await WaitFor(second, s => s.Status == LobbyStatus.Open && s.Queue.Count == 3 && s.Host == "p2");
        Assert.Equal(["p2", "p1", "p4"], restored.Queue);
        Assert.Equal(roomId, restored.RoomId);
    }

    [Fact]
    public async Task A_room_that_closed_while_the_bot_was_down_is_made_again()
    {
        var first = await StartAsync(NoMatchHistory.Instance);
        var roomId = await PlayUntilMidMatchAsync(first);
        await first.KillAsync();
        _server.RemoveRoom(roomId);

        var second = await StartAsync(NoMatchHistory.Instance);
        var remade = await WaitFor(second, s => s.Status == LobbyStatus.Open && s.RoomId != roomId);
        Assert.Equal(_server.LastRoomId, remade.RoomId);
        Assert.Equal("my room", remade.Title);
        Assert.Empty(remade.Queue);

        await second.Actor.DrainAsync().WaitAsync(Timeout);
        Assert.Equal(remade.RoomId, (await new SqliteLobbyStore(DatabasePath).LoadAsync(default)).Session?.RoomId);
    }

    [Fact]
    public async Task A_restart_with_no_saved_room_stays_idle()
    {
        var process = await StartAsync(NoMatchHistory.Instance);
        await Task.Delay(300);
        Assert.Equal(LobbyStatus.Idle, process.Actor.Snapshot.Status);
        Assert.DoesNotContain(_server.Received, r => r.Line.StartsWith("JOIN", StringComparison.Ordinal) || r.Line.Contains("!mp make", StringComparison.Ordinal));
    }

    private sealed class Process
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _run;
        private readonly BanchoIrcTransport _transport;
        private bool _killed;

        public Process(LobbyActor actor, BanchoIrcTransport transport, RecoveredLobby recovered)
        {
            Actor = actor;
            _transport = transport;
            Recovered = recovered;
            _run = actor.RunAsync(_stop.Token);
        }

        public LobbyActor Actor { get; }

        public RecoveredLobby Recovered { get; }

        public async Task KillAsync()
        {
            if (_killed)
            {
                return;
            }

            _killed = true;
            await _stop.CancelAsync();
            await _run;
            _transport.Dispose();
            _stop.Dispose();
        }
    }

    private sealed class RunningGameHistory(long roomId) : IMatchHistory
    {
        public static readonly DateTimeOffset Started = new(2026, 1, 1, 9, 58, 0, TimeSpan.Zero);

        public ValueTask<MatchLookup> GetAsync(long matchId, long? afterEventId, CancellationToken cancellationToken)
        {
            var game = new MatchGame(902, Started, null, 4242, []);
            return ValueTask.FromResult(matchId == roomId
                ? MatchLookup.Found(new MatchHistory(roomId, "my room", Started, null, 902, 2, [new MatchEvent(2, "other", Started, null, game)], new Dictionary<int, string>()))
                : MatchLookup.NotFound);
        }
    }

    private sealed class NoCatalog : IBeatmapCatalog
    {
        public ValueTask<BeatmapLookup> FindAsync(int beatmapId, GameMode mode, bool allowConvert, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new BeatmapLookup(BeatmapLookupOutcome.Failed, null));
    }
}
