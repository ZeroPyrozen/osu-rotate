using System.Security.Claims;
using System.Threading.Channels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OsuRotate.Application;
using OsuRotate.Domain;
using OsuRotate.Web.Bot;
using OsuRotate.Web.Components.Lobby;
using OsuRotate.Web.Components.Pages;

namespace OsuRotate.Web.Tests;

// Phase 5, issue 02: the lobby page, rendered with ASP.NET Core's HtmlRenderer.
public sealed class LobbyViewTests : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly ServiceProvider _services;
    private readonly HtmlRenderer _renderer;

    public LobbyViewTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<LobbyRuntime>();
        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();
        services.AddSingleton<AuthenticationStateProvider, OwnerState>();
        _services = services.BuildServiceProvider();
        _renderer = new HtmlRenderer(_services, _services.GetRequiredService<ILoggerFactory>());
    }

    public async ValueTask DisposeAsync()
    {
        await _renderer.DisposeAsync();
        await _services.DisposeAsync();
    }

    private LobbyRuntime Runtime => _services.GetRequiredService<LobbyRuntime>();

    private Task<string> RenderAsync(LobbySnapshot? snapshot) =>
        _renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await _renderer.RenderComponentAsync<LobbyView>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Snapshot"] = snapshot }));
            return output.ToHtmlString();
        });

    private static LobbySnapshot Snapshot(
        LobbyStatus status = LobbyStatus.Open,
        bool connected = true,
        long? roomId = 53084403,
        string? title = "5* host rotate",
        bool closing = false,
        IReadOnlyList<PlayerSnapshot>? players = null,
        string? host = "p1",
        IReadOnlyList<string>? queue = null,
        bool matching = false,
        int beatmapId = 0,
        IReadOnlyList<string>? referees = null,
        bool countdown = false) =>
        new(status, connected, roomId, title, closing,
            players ?? [new("p1", 11, 1, Team.None, MatchStatus.InLobby), new("p 2", null, 2, Team.Blue, MatchStatus.Playing)],
            host, queue ?? ["p1", "p 2"], matching, countdown, beatmapId, [], referees ?? [], new LobbySettings { OwnerName = "creator" });

    [Fact]
    public async Task Shows_the_room_queue_players_and_host()
    {
        var html = await RenderAsync(Snapshot(matching: true, beatmapId: 4242));

        Assert.Contains("Lobby status: <strong>Open</strong>", html, StringComparison.Ordinal);
        Assert.Contains("Bancho: <strong>connected</strong>", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://osu.ppy.sh/mp/53084403\"", html, StringComparison.Ordinal);
        Assert.Contains("5* host rotate (#53084403)", html, StringComparison.Ordinal);
        Assert.Contains("A match is being played.", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://osu.ppy.sh/b/4242\"", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"count\">2 / 16</span>", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://osu.ppy.sh/users/11\"", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"team blue\">Blue</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"state playing\">Playing</span>", html, StringComparison.Ordinal);
        Assert.Equal(1, html.Split("<li class=\"host\">").Length - 1);
        Assert.Contains("<span class=\"badge host\">Host</span>", html, StringComparison.Ordinal);
        Assert.True(html.IndexOf("p1", StringComparison.Ordinal) < html.IndexOf("p 2", StringComparison.Ordinal));
    }

    // The same players used to be listed four times: the queue, the queue editor, the players
    // table and moderation. Now each is listed once, with every action on that row.
    [Fact]
    public async Task Each_player_is_listed_once_with_every_action_on_their_row()
    {
        var html = await RenderAsync(Snapshot(
            players:
            [
                new("p1", 11, 1, Team.None, MatchStatus.InLobby),
                new("p2", 12, 2, Team.None, MatchStatus.InLobby),
                new("p3", 13, 3, Team.None, MatchStatus.InLobby),
                new("banned", 14, 4, Team.None, MatchStatus.InLobby),
            ],
            queue: ["p1", "p2", "p3"]) with { Denied = ["banned"] });

        Assert.Equal(4, html.Split("<li").Length - 1);
        Assert.Equal(1, html.Split(">p2</a>").Length - 1);

        // The first cannot move up, the last cannot move down, and the host is not offered host.
        Assert.Matches("aria-label=\"Move p1 up\"[^>]*disabled|disabled[^>]*aria-label=\"Move p1 up\"", html);
        Assert.Matches("aria-label=\"Move p3 down\"[^>]*disabled|disabled[^>]*aria-label=\"Move p3 down\"", html);
        Assert.Equal(2, html.Split("Give host").Length - 1);
        Assert.Equal(4, html.Split(">Kick<").Length - 1);

        // Someone who is never given host is in the room but not in the queue.
        Assert.Equal(3, html.Split("Never host</button>").Length - 1);
        Assert.Equal(1, html.Split("Allow host</button>").Length - 1);
        Assert.DoesNotContain("aria-label=\"Move banned up\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_roster_is_the_queue_in_order_then_everyone_else_by_slot()
    {
        var rows = PlayerRoster.Rows(Snapshot(
            players:
            [
                new("late", null, 9, Team.None, MatchStatus.InLobby),
                new("b", null, 2, Team.None, MatchStatus.InLobby),
                new("a", null, 1, Team.None, MatchStatus.InLobby),
                new("early", null, 3, Team.None, MatchStatus.InLobby),
            ],
            host: "b",
            queue: ["b", "a"],
            referees: ["a"]) with { Denied = ["late"] });

        Assert.Equal(["b", "a", "early", "late"], rows.Select(r => r.Name));
        Assert.Equal([1, 2, null, null], rows.Select(r => r.Position));
        Assert.Equal([true, false, false, false], rows.Select(r => r.IsHost));
        Assert.Equal([false, true, false, false], rows.Select(r => r.IsReferee));
        Assert.Equal([false, false, false, true], rows.Select(r => r.IsDenied));
    }

    [Fact]
    public async Task The_player_count_is_out_of_the_kept_room_size()
    {
        var snapshot = Snapshot();
        Assert.Equal(16, PlayerRoster.Capacity(snapshot));

        var kept = snapshot with { Settings = snapshot.Settings with { Keeping = new RoomKeeping { Size = 8 } } };
        Assert.Equal(8, PlayerRoster.Capacity(kept));
        Assert.Contains("<span class=\"count\">2 / 8</span>", await RenderAsync(kept), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Players_cannot_be_acted_on_while_the_bot_is_out_of_the_room()
    {
        var html = await RenderAsync(Snapshot(status: LobbyStatus.Reconnecting, connected: false));
        Assert.Contains("p1", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<button", html, StringComparison.Ordinal);
        Assert.Contains("Bancho dropped the connection.", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LobbyStatus.Connecting, "Connecting to Bancho.")]
    [InlineData(LobbyStatus.Creating, "Asking Bancho for a new room.")]
    [InlineData(LobbyStatus.Rejoining, "Joining the room.")]
    [InlineData(LobbyStatus.Syncing, "Reading who is in the room.")]
    [InlineData(LobbyStatus.Reconnecting, "Bancho dropped the connection.")]
    [InlineData(LobbyStatus.Closing, "Closing the room.")]
    public void Every_status_but_open_is_explained_in_a_sentence(LobbyStatus status, string expected)
    {
        Assert.StartsWith(expected, LobbyView.StatusHelp(Snapshot(status: status)), StringComparison.Ordinal);
        Assert.Null(LobbyView.StatusHelp(Snapshot()));
        Assert.Null(LobbyView.StatusHelp(null));
    }

    [Fact]
    public async Task A_bot_that_was_never_given_a_login_says_so_instead_of_a_status_hint()
    {
        Runtime.Problem = LobbyHostService.NotConfigured;
        var html = await RenderAsync(Snapshot(status: LobbyStatus.Idle, connected: false, roomId: null, title: null, players: [], host: null, queue: []));

        Assert.Contains("role=\"alert\"", html, StringComparison.Ordinal);
        Assert.Contains("The bot has no Bancho login", html, StringComparison.Ordinal);
        Assert.DoesNotContain("You can create a room now", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Says_when_nothing_is_open_or_connected()
    {
        var html = await RenderAsync(Snapshot(status: LobbyStatus.Idle, connected: false, roomId: null, title: null, players: [], host: null, queue: []));

        Assert.Contains("Lobby status: <strong>Idle</strong>", html, StringComparison.Ordinal);
        Assert.Contains("Bancho: <strong>not connected</strong>", html, StringComparison.Ordinal);
        Assert.Contains("No room open.", html, StringComparison.Ordinal);
        Assert.Contains("The bot is not connected to Bancho yet.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("osu.ppy.sh/b/", html, StringComparison.Ordinal);

        // With no room there is no host to wait for and no player list to show.
        Assert.DoesNotContain("Waiting for the host.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("lobby-players", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Before_the_bot_has_started_it_says_so()
    {
        var html = await RenderAsync(null);
        Assert.Contains("Lobby status: <strong>Idle</strong>", html, StringComparison.Ordinal);
        Assert.Contains("The bot is starting.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shows_closing_when_empty_and_referees()
    {
        var html = await RenderAsync(Snapshot(closing: true, referees: ["creator", "p_2"]));

        Assert.Contains("Closing once everyone has left.", html, StringComparison.Ordinal);
        Assert.Contains("creator, p_2", html, StringComparison.Ordinal);
        Assert.Contains("p 2", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"badge\">Referee</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Names_and_titles_from_bancho_are_shown_as_text()
    {
        var html = await RenderAsync(Snapshot(
            title: "<script>alert(1)</script>",
            players: [new("<b>evil</b>", null, 1, Team.None, MatchStatus.InLobby)],
            host: "<b>evil</b>",
            queue: ["<b>evil</b>"]));

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>evil</b>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;evil&lt;/b&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_page_follows_the_lobby_as_it_changes_and_stops_listening_when_closed()
    {
        var transport = new ScriptedTransport();
        var engine = new LobbyEngine(new LobbySettings { OwnerName = "creator", HostAfkCheckInterval = TimeSpan.Zero }, TimeProvider.System);
        var actor = new LobbyActor(engine, transport, new NoCatalog(), TimeProvider.System, lifecycle: new LobbyLifecycle(7, "room"));
        using var stop = new CancellationTokenSource();
        var run = actor.RunAsync(stop.Token);
        Runtime.Attach(actor);

        var page = await _renderer.Dispatcher.InvokeAsync(() => _renderer.RenderComponentAsync<Home>());
        string Html() => _renderer.Dispatcher.InvokeAsync(page.ToHtmlString).Result;
        Assert.Contains("Lobby status: <strong>Connecting</strong>", Html(), StringComparison.Ordinal);
        Assert.Equal(1, Runtime.ListenerCount);

        transport.Emit(new TransportConnected());
        transport.Emit(new RoomJoined(7));
        transport.Emit(new RoomStateReceived(new RoomState("room", 7, 0, string.Empty, "HeadToHead", "Score", string.Empty,
            [new RoomPlayer(1, "Not Ready", 11, "alice", true, Team.None, "Host")])));
        await WaitUntilAsync(() => Html().Contains("Lobby status: <strong>Open</strong>", StringComparison.Ordinal) && Html().Contains("alice", StringComparison.Ordinal));

        transport.Emit(new PlayerJoined("bob", 2, Team.None));
        await WaitUntilAsync(() => Html().Contains("bob", StringComparison.Ordinal));

        // The bot stopping shows too.
        Runtime.Attach(null);
        await WaitUntilAsync(() => Html().Contains("The bot is starting.", StringComparison.Ordinal));

        await _renderer.Dispatcher.InvokeAsync(() => _renderer.Dispose());
        Assert.Equal(0, Runtime.ListenerCount);

        await stop.CancelAsync();
        await run;
    }

    [Fact]
    public async Task A_stopped_bots_actor_no_longer_reaches_the_pages()
    {
        var transport = new ScriptedTransport();
        var engine = new LobbyEngine(new LobbySettings { OwnerName = "creator", HostAfkCheckInterval = TimeSpan.Zero }, TimeProvider.System);
        var actor = new LobbyActor(engine, transport, new NoCatalog(), TimeProvider.System);
        using var stop = new CancellationTokenSource();
        var run = actor.RunAsync(stop.Token);
        Runtime.Attach(actor);
        Runtime.Attach(null);

        var seen = new List<LobbySnapshot?>();
        Runtime.Changed += seen.Add;
        transport.Emit(new TransportConnected());
        await WaitUntilAsync(() => actor.Snapshot.Connected);
        await actor.DrainAsync().WaitAsync(Timeout);

        Assert.Empty(seen);
        await stop.CancelAsync();
        await run;
    }

    [Fact]
    public async Task A_page_that_never_renders_again_does_not_hold_up_the_lobby()
    {
        var transport = new ScriptedTransport();
        var engine = new LobbyEngine(new LobbySettings { OwnerName = "creator", HostAfkCheckInterval = TimeSpan.Zero }, TimeProvider.System);
        var actor = new LobbyActor(engine, transport, new NoCatalog(), TimeProvider.System, lifecycle: new LobbyLifecycle(7, "room"));
        using var stop = new CancellationTokenSource();
        var run = actor.RunAsync(stop.Token);
        Runtime.Attach(actor);
        await _renderer.Dispatcher.InvokeAsync(() => _renderer.RenderComponentAsync<Home>());

        // Hold the page's dispatcher with blocking work, as a stuck render would. The actor must keep
        // handling events (Rejoining, then Syncing) regardless.
        using var release = new ManualResetEventSlim();
        var blocked = new TaskCompletionSource();
        var holder = Task.Run(() => _renderer.Dispatcher.InvokeAsync(() =>
        {
            blocked.SetResult();
            release.Wait(Timeout);
        }));
        await blocked.Task.WaitAsync(Timeout);

        transport.Emit(new TransportConnected());
        transport.Emit(new RoomJoined(7));
        await WaitUntilAsync(() => actor.Snapshot.Status == LobbyStatus.Syncing);
        Assert.Contains(transport.Sent, c => c is RequestRoomState);

        release.Set();
        await holder;
        await stop.CancelAsync();
        await run;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class OwnerState : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "owner")], "test"))));
    }

    private sealed class ScriptedTransport : ILobbyTransport
    {
        private readonly Channel<LobbyEvent> _events = Channel.CreateUnbounded<LobbyEvent>();

        public System.Collections.Concurrent.ConcurrentQueue<LobbyCommand> Sent { get; } = new();

        public TransportCapabilities Capabilities { get; } = new(CanSetHost: true, HasChat: true);

        public void Emit(LobbyEvent lobbyEvent) => _events.Writer.TryWrite(lobbyEvent);

        public IAsyncEnumerable<LobbyEvent> Events(CancellationToken cancellationToken) => _events.Reader.ReadAllAsync(cancellationToken);

        public ValueTask SendAsync(LobbyCommand command, CancellationToken cancellationToken)
        {
            Sent.Enqueue(command);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoCatalog : IBeatmapCatalog
    {
        public ValueTask<BeatmapLookup> FindAsync(int beatmapId, GameMode mode, bool allowConvert, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new BeatmapLookup(BeatmapLookupOutcome.Failed, null));
    }
}
