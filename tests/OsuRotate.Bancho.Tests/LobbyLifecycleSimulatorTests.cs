using Microsoft.Extensions.Logging.Abstractions;
using OsuRotate.Application;
using OsuRotate.Domain;
using OsuRotate.Infrastructure.Bancho;
using OsuRotate.Infrastructure.Persistence;

namespace OsuRotate.Bancho.Tests;

// The lobby actor, its lifecycle and the real IRC transport against the Bancho simulator.
public sealed class LobbyLifecycleSimulatorTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly BanchoSimulator _server = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "osu-rotate-tests", Guid.NewGuid().ToString("N"));
    private BanchoIrcTransport? _transport;
    private LobbyActor? _actor;
    private Task? _run;

    private LobbyActor Actor => _actor!;

    public ValueTask InitializeAsync()
    {
        _server.Start();
        _transport = new BanchoIrcTransport(
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
        var engine = new LobbyEngine(new LobbySettings { OwnerName = "creator", HostAfkCheckInterval = TimeSpan.Zero }, TimeProvider.System);
        Directory.CreateDirectory(_folder);
        _actor = new LobbyActor(engine, _transport, new NoCatalog(), TimeProvider.System, store: new SqliteLobbyStore(DatabasePath));
        _run = _actor.RunAsync(_stop.Token);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_run is not null)
        {
            await _run;
        }

        _transport?.Dispose();
        await _server.DisposeAsync();
        _stop.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Left for the OS to clean up.
        }
    }

    private string DatabasePath => Path.Combine(_folder, "rotate.db");

    private async Task<LobbySnapshot> WaitFor(Func<LobbySnapshot, bool> condition)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        while (!condition(Actor.Snapshot))
        {
            await Task.Delay(10, timeout.Token);
        }

        return Actor.Snapshot;
    }

    private async Task WaitForLine(string line, int count = 1)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        while (Lines(line) < count)
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private int Lines(string line) => _server.Received.Count(r => r.Line == line);

    private async Task<long> CreateAsync(string title = "my room")
    {
        await Actor.PostAsync(new CreateLobby(title));
        var open = await WaitFor(s => s.Status == LobbyStatus.Open);
        return open.RoomId!.Value;
    }

    [Fact]
    public async Task Creates_a_lobby_once_connected()
    {
        var roomId = await CreateAsync("5* rotate");

        Assert.Equal(_server.LastRoomId, roomId);
        Assert.Equal(1, Lines("PRIVMSG BanchoBot :!mp make 5* rotate"));
        Assert.Equal("5* rotate", Actor.Snapshot.Title);
    }

    [Fact]
    public async Task The_room_and_queue_are_in_the_database_for_the_next_start()
    {
        var roomId = await CreateAsync();
        _server.AddPlayer("p1", 11);
        _server.AddPlayer("p2", 22);
        await WaitFor(s => s.Queue.Count == 2 && s.Host == "p1");
        await Actor.DrainAsync().WaitAsync(Timeout);

        var saved = await new SqliteLobbyStore(DatabasePath).LoadAsync(default);
        Assert.Equal(new SavedSession(roomId, "my room", ["p1", "p2"], "p1"), saved.Session);
        Assert.Equal("creator", saved.Settings!.OwnerName);
    }

    [Fact]
    public async Task Attaches_to_an_existing_room_and_syncs_it()
    {
        var roomId = _server.LastRoomId;
        _server.AddRoom(roomId);
        _server.AddPlayer("p1", 11);
        _server.AddPlayer("p2", 22);

        await Actor.PostAsync(new AttachLobby(roomId));
        var open = await WaitFor(s => s.Status == LobbyStatus.Open && s.Players.Count == 2);

        Assert.Equal(roomId, open.RoomId);
        Assert.Equal("test room", open.Title);
        await WaitForLine($"PRIVMSG #mp_{roomId} :!mp listrefs");
        Assert.Contains(_server.Received, r => r.Line == $"PRIVMSG #mp_{roomId} :!mp settings");
        await WaitFor(s => s.Referees.Contains("creator"));
    }

    [Fact]
    public async Task Attaching_to_a_room_that_is_gone_leaves_the_bot_idle()
    {
        await WaitForConnectedAsync();
        await Actor.PostAsync(new AttachLobby(424242));
        await WaitForLine("JOIN #mp_424242");
        var idle = await WaitFor(s => s.Status == LobbyStatus.Idle);

        Assert.Null(idle.RoomId);
        Assert.Equal(0, _server.Received.Count(r => r.Line.Contains("!mp make", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Closes_the_lobby()
    {
        var roomId = await CreateAsync();
        await Actor.PostAsync(new CloseLobby());

        var idle = await WaitFor(s => s.Status == LobbyStatus.Idle);
        Assert.Null(idle.RoomId);
        Assert.Equal(1, Lines($"PRIVMSG #mp_{roomId} :!mp close"));
        Assert.Equal(1, Lines("PRIVMSG BanchoBot :!mp make my room"));
    }

    [Fact]
    public async Task Closes_when_the_last_player_leaves()
    {
        var roomId = await CreateAsync();
        _server.AddPlayer("p1", 11);
        await WaitFor(s => s.Players.Count == 1);

        await Actor.PostAsync(new CloseLobbyWhenEmpty(true));
        await WaitFor(s => s.ClosingWhenEmpty);
        Assert.Contains(_server.Received, r => r.Line.StartsWith($"PRIVMSG #mp_{roomId} :!mp password ", StringComparison.Ordinal));
        Assert.Equal(0, Lines($"PRIVMSG #mp_{roomId} :!mp close"));

        _server.RemovePlayer("p1");
        await WaitFor(s => s.Status == LobbyStatus.Idle);
        Assert.Equal(1, Lines($"PRIVMSG #mp_{roomId} :!mp close"));
    }

    [Fact]
    public async Task A_dropped_connection_rejoins_the_same_room_and_keeps_the_queue()
    {
        var roomId = await CreateAsync();
        _server.AddPlayer("p1", 11);
        _server.AddPlayer("p2", 22);
        await WaitFor(s => s.Queue.Count == 2);

        _server.DropClient();
        await WaitFor(s => s.Status == LobbyStatus.Reconnecting);
        var open = await WaitFor(s => s.Status == LobbyStatus.Open);

        Assert.Equal(roomId, open.RoomId);
        Assert.Equal(["p1", "p2"], open.Queue);
        Assert.Equal(2, _server.Connections);
        await WaitForLine($"JOIN #mp_{roomId}");
        await WaitForLine($"PRIVMSG #mp_{roomId} :!mp settings");
        Assert.Equal(1, Lines("PRIVMSG BanchoBot :!mp make my room"));
    }

    [Fact]
    public async Task A_room_that_vanished_while_away_is_made_again()
    {
        var first = await CreateAsync();
        _server.RemoveRoom(first);
        _server.DropClient();

        await WaitForLine($"JOIN #mp_{first}");
        var open = await WaitFor(s => s.Status == LobbyStatus.Open && s.RoomId != first);
        Assert.Equal(_server.LastRoomId, open.RoomId);
        Assert.Equal(2, Lines("PRIVMSG BanchoBot :!mp make my room"));
    }

    [Fact]
    public async Task A_room_bancho_closes_is_made_again()
    {
        var first = await CreateAsync();
        _server.CloseRoomFromBancho(first);

        var open = await WaitFor(s => s.Status == LobbyStatus.Open && s.RoomId != first);
        Assert.Equal(_server.LastRoomId, open.RoomId);
    }

    [Fact]
    public async Task The_owner_closing_in_game_is_left_closed()
    {
        var roomId = await CreateAsync();
        _server.Chat("creator", $"#mp_{roomId}", "!mp close");
        _server.FromBot($"#mp_{roomId}", "Closed the match");
        _server.Send($":creator!cho@ppy.sh PART :#mp_{roomId}");

        await WaitFor(s => s.Status == LobbyStatus.Idle);
        await Task.Delay(100);
        Assert.Equal(LobbyStatus.Idle, Actor.Snapshot.Status);
        Assert.Equal(1, Lines("PRIVMSG BanchoBot :!mp make my room"));
    }

    private async Task WaitForConnectedAsync()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        while (!_server.Received.Any(r => r.Line.StartsWith("USER ", StringComparison.Ordinal)))
        {
            await Task.Delay(10, timeout.Token);
        }

        // The MOTD has been sent once USER is in; give the transport a moment to read it.
        await Task.Delay(100, timeout.Token);
    }

    private sealed class NoCatalog : IBeatmapCatalog
    {
        public ValueTask<BeatmapLookup> FindAsync(int beatmapId, GameMode mode, bool allowConvert, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new BeatmapLookup(BeatmapLookupOutcome.Failed, null));
    }
}
