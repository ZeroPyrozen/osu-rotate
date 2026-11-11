using OsuRotate.Domain;

namespace OsuRotate.Domain.Tests;

internal sealed class ManualTime : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

// Drives a LobbyEngine with a fake room that answers commands the way Bancho does. While Holding,
// Bancho's answers wait until Release, which replays osu-ahr's latency races deterministically.
internal sealed class LobbyHarness
{
    public const string Owner = "creator";

    private readonly List<string> _players = [];
    private readonly Queue<LobbyEvent> _held = new();
    private string? _host;
    private bool _matching;
    private int _nextBeatmap = 100;

    public LobbyHarness()
    {
        Engine = new LobbyEngine(new LobbySettings { OwnerName = Owner }, Time);
    }

    public ManualTime Time { get; } = new();
    public LobbyEngine Engine { get; }
    public List<LobbyCommand> Sent { get; } = [];
    public bool Holding { get; set; }

    public IReadOnlyList<string> Queue => [.. Engine.Rotation.Queue.Select(p => p.Name)];
    public IEnumerable<string> Said => Sent.OfType<Say>().Select(s => s.Text);

    public void Feed(LobbyEvent lobbyEvent) => Run(() => Engine.Handle(lobbyEvent));

    public void Run(Func<IReadOnlyList<LobbyCommand>> action)
    {
        var pending = new Queue<LobbyEvent>();
        Process(action());
        while (pending.TryDequeue(out var next))
        {
            Process(Engine.Handle(next));
        }

        void Process(IReadOnlyList<LobbyCommand> commands)
        {
            foreach (var command in commands)
            {
                Sent.Add(command);
                foreach (var reply in Answer(command))
                {
                    (Holding ? _held : pending).Enqueue(reply);
                }
            }
        }
    }

    public void Release()
    {
        Holding = false;
        while (_held.TryDequeue(out var reply))
        {
            Feed(reply);
        }
    }

    public string[] AddPlayers(params string[] names)
    {
        foreach (var name in names)
        {
            _players.Add(name);
            Feed(new PlayerJoined(name, _players.Count, Team.None));
        }

        return names;
    }

    public string[] AddPlayers(int count) => AddPlayers([.. Enumerable.Range(_players.Count, count).Select(i => $"p{i}")]);

    public void RemovePlayer(string name)
    {
        _players.Remove(name);
        if (_host == name)
        {
            _host = null;
        }

        Feed(new PlayerLeft(name));
    }

    public void ChangeMap()
    {
        Feed(new BeatmapChanging());
        Feed(new BeatmapChanged(_nextBeatmap++, "artist - title [diff]", false));
    }

    public void StartMatch()
    {
        _matching = true;
        Feed(new MatchStarted());
    }

    public void FinishMatch()
    {
        foreach (var player in _players.ToList())
        {
            Feed(new PlayerFinished(player, 100000, true));
        }

        _matching = false;
        Feed(new MatchFinished());
    }

    public void PlayMatch()
    {
        StartMatch();
        FinishMatch();
    }

    // Bancho announces the abort itself, as when the host or a referee aborts.
    public void AbortMatch(params string[] finishers)
    {
        foreach (var player in finishers)
        {
            Feed(new PlayerFinished(player, 100000, true));
        }

        _matching = false;
        Feed(new MatchAborted());
    }

    public void PlayMatchAndAbort(params string[] finishers)
    {
        StartMatch();
        AbortMatch(finishers);
    }

    // A player handing host over in the game, not the bot.
    public void ChangeHostManually(string name)
    {
        _host = name;
        Feed(new HostChanged(name));
    }

    public void ApplyRoomState(RoomState state)
    {
        _players.Clear();
        _players.AddRange(state.Players.OrderBy(p => p.Slot).Select(p => p.Username));
        _host = state.Players.FirstOrDefault(p => p.IsHost)?.Username;
        Feed(new RoomStateReceived(state));
    }

    public void Chat(string name, string text) => Feed(new ChatReceived(name, text, false));

    public void AssertHost(string name)
    {
        Assert.Equal(name, Engine.Room.Host?.Name);
        Assert.Equal(name, _host);
    }

    public void AssertNoHost()
    {
        Assert.Null(Engine.Room.Host);
        Assert.Null(_host);
    }

    // The states osu-ahr's tests use: s0 empty, s1 no host yet, hr host who rotates after the
    // next match, hn host who keeps the turn once more, m match in progress.
    public void AssertState(string state)
    {
        var rotation = Engine.Rotation;
        var room = Engine.Room;
        switch (state)
        {
            case "s0":
                Assert.Empty(rotation.Queue);
                break;
            case "s1":
                Assert.NotEmpty(rotation.Queue);
                Assert.False(room.IsMatching);
                Assert.Null(room.Host);
                break;
            case "hr":
                Assert.NotEmpty(rotation.Queue);
                Assert.False(room.IsMatching);
                Assert.True(rotation.NeedsRotate, "expected the host to rotate after the next match");
                Assert.NotNull(room.Host);
                break;
            case "hn":
                Assert.NotEmpty(rotation.Queue);
                Assert.False(room.IsMatching);
                Assert.False(rotation.NeedsRotate, "expected the host to keep the turn");
                Assert.NotNull(room.Host);
                break;
            case "m":
                Assert.NotEmpty(rotation.Queue);
                Assert.True(room.IsMatching);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }
    }

    private IEnumerable<LobbyEvent> Answer(LobbyCommand command)
    {
        switch (command)
        {
            case TransferHost c when _players.Contains(c.Username):
                _host = c.Username;
                yield return new HostChanged(c.Username);
                break;
            case TransferHost:
                yield return new UserNotFound();
                break;
            case ClearHost:
                _host = null;
                yield return new HostCleared();
                break;
            case Domain.AbortMatch:
                yield return _matching ? new MatchAborted() : new MatchNotInProgress();
                _matching = false;
                break;
            case Domain.StartMatch:
                _matching = true;
                yield return new MatchStarted();
                break;
            case RequestRoomState:
                yield return new RoomStateReceived(new RoomState("room", 1, 100, "map", "HeadToHead", "Score", "Freemod",
                    [.. _players.Select((name, i) => new RoomPlayer(i + 1, "Not Ready", i + 1, name, name == _host, Team.None, string.Empty))]));
                break;
        }
    }
}

internal static class RoomStates
{
    private static RoomState State(params (int Slot, string Name, bool Host)[] players) =>
        new("5* host rotate", 53084403, 853167, "Silent Siren - Hachigatsu no Yoru [August]", "HeadToHead", "Score", "Freemod",
            [.. players.Select(p => new RoomPlayer(p.Slot, "Not Ready", p.Slot * 111, p.Name, p.Host, Team.None, p.Host ? "Host" : string.Empty))]);

    // The same rooms as osu-ahr's MpSettingsCases.
    public static RoomState Case1_1 => State((1, "p1", true), (2, "p2", false), (3, "p3", false), (4, "p4", false), (5, "p5", false));
    public static RoomState Case1_2 => State((1, "p1", false), (2, "p2", false), (3, "p3", true), (4, "p4", false), (5, "p5", false));
    public static RoomState Case1_3 => State((1, "p6", false), (2, "p2", false), (4, "p4", true), (5, "p5", false), (6, "p7", false));
}
