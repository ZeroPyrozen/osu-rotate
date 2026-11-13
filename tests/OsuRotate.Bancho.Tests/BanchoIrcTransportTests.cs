using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using OsuRotate.Domain;
using OsuRotate.Infrastructure.Bancho;

namespace OsuRotate.Bancho.Tests;

public sealed class BanchoIrcTransportTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly BanchoSimulator _server = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<LobbyEvent> _events = Channel.CreateUnbounded<LobbyEvent>();
    private BanchoIrcTransport? _transport;
    private Task? _collector;

    private BanchoIrcTransport Transport => _transport!;

    public ValueTask InitializeAsync()
    {
        _server.Start();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_collector is not null)
        {
            await _collector;
        }

        _transport?.Dispose();
        await _server.DisposeAsync();
        _stop.Dispose();
    }

    private void StartTransport(string password = "secret", int messagesPerWindow = 8, TimeSpan? rateWindow = null, TimeSpan? idlePingAfter = null, TimeSpan? deadAfter = null, Microsoft.Extensions.Logging.ILogger<BanchoIrcTransport>? logger = null)
    {
        _transport = new BanchoIrcTransport(
            new BanchoOptions
            {
                Host = "127.0.0.1",
                Port = _server.Port,
                Nick = "creator",
                Password = password,
                MessagesPerWindow = messagesPerWindow,
                RateWindow = rateWindow ?? TimeSpan.FromSeconds(5),
                IdlePingAfter = idlePingAfter ?? TimeSpan.FromMinutes(2),
                DeadAfter = deadAfter ?? TimeSpan.FromMinutes(5),
                ReconnectBaseDelay = TimeSpan.FromMilliseconds(20),
                ReconnectMaxDelay = TimeSpan.FromMilliseconds(50),
            },
            logger ?? NullLogger<BanchoIrcTransport>.Instance);

        _collector = Task.Run(async () =>
        {
            try
            {
                await foreach (var lobbyEvent in Transport.Events(_stop.Token))
                {
                    await _events.Writer.WriteAsync(lobbyEvent);
                }
            }
            catch (OperationCanceledException)
            {
                // The test is over.
            }

            _events.Writer.TryComplete();
        });
    }

    // Reads forward, skipping other events, until one of type T arrives.
    private async Task<T> Next<T>(Func<T, bool>? predicate = null)
        where T : LobbyEvent
    {
        using var timeout = new CancellationTokenSource(Timeout);
        await foreach (var lobbyEvent in _events.Reader.ReadAllAsync(timeout.Token))
        {
            if (lobbyEvent is T match && (predicate is null || predicate(match)))
            {
                return match;
            }
        }

        throw new InvalidOperationException($"The event stream ended before a {typeof(T).Name} arrived.");
    }

    private async Task AssertNoEvent<T>(TimeSpan during)
        where T : LobbyEvent
    {
        using var timeout = new CancellationTokenSource(during);
        try
        {
            await foreach (var lobbyEvent in _events.Reader.ReadAllAsync(timeout.Token))
            {
                Assert.False(lobbyEvent is T, $"Did not expect {lobbyEvent}.");
            }
        }
        catch (OperationCanceledException)
        {
            // Nothing arrived in time, as expected.
        }
    }

    private async Task WaitForReceived(Func<IReadOnlyList<(DateTimeOffset At, string Line)>, bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow + Timeout;
        while (!condition(_server.Received))
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, "The server did not receive the expected lines.");
            await Task.Delay(10);
        }
    }

    private async Task<long> ConnectAndCreateRoom()
    {
        StartTransport();
        await Next<TransportConnected>();
        await Transport.SendAsync(new CreateRoom("test room"), default);
        return (await Next<RoomJoined>()).RoomId;
    }

    [Fact]
    public async Task Authenticates_with_pass_nick_and_user_in_order()
    {
        StartTransport();
        await Next<TransportConnected>();

        Assert.Equal(["PASS secret", "NICK creator", "USER creator 0 * :osu-rotate"], _server.Received.Take(3).Select(r => r.Line));
    }

    // osu-ahr treated 001 as connected, but channels are only rejoined at the end of the MOTD.
    [Fact]
    public async Task Is_not_connected_until_the_MOTD_ends()
    {
        _server.MotdGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        StartTransport();
        await WaitForReceived(lines => lines.Any(l => l.Line.StartsWith("USER", StringComparison.Ordinal)));

        await AssertNoEvent<TransportConnected>(TimeSpan.FromMilliseconds(200));

        _server.MotdGate.SetResult();
        await Next<TransportConnected>();
    }

    // Real Bancho rejects with MOTD text and a closed connection; standard IRC servers send 464.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_wrong_password_stops_without_retrying(bool rejectWithNumeric)
    {
        _server.RejectWithNumeric = rejectWithNumeric;
        StartTransport(password: "wrong");

        await Next<TransportAuthenticationFailed>();
        await Task.Delay(300);

        Assert.Equal(1, _server.Connections);
    }

    [Fact]
    public async Task A_connection_closed_before_login_without_a_rejection_is_retried()
    {
        _server.MotdGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        StartTransport();
        await WaitForReceived(lines => lines.Any(l => l.Line.StartsWith("USER", StringComparison.Ordinal)));

        _server.DropClient();
        _server.MotdGate.SetResult();

        await Next<TransportConnected>();
        Assert.Equal(2, _server.Connections);
    }

    // Anything unforeseen inside a session (here, a log sink that throws) used to end the transport
    // for good: the bot stayed up but never spoke to Bancho again until the service was restarted.
    [Fact]
    public async Task An_unexpected_failure_in_a_session_reconnects()
    {
        StartTransport(logger: new FailsOnce());

        await Next<TransportConnected>();
        Assert.Equal(2, _server.Connections);
    }

    private sealed class FailsOnce : Microsoft.Extensions.Logging.ILogger<BanchoIrcTransport>
    {
        private int _failed;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (formatter(state, exception) == "Connected to Bancho." && Interlocked.Exchange(ref _failed, 1) == 0)
            {
                throw new InvalidOperationException("The log sink broke.");
            }
        }
    }

    [Fact]
    public async Task Answers_server_pings()
    {
        StartTransport();
        await Next<TransportConnected>();

        _server.Send("PING cho.ppy.sh");

        await WaitForReceived(lines => lines.Any(l => l.Line == "PONG :cho.ppy.sh"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Creates_a_room_whichever_reply_arrives_first(bool createdReplyFirst)
    {
        _server.CreatedReplyBeforeJoin = createdReplyFirst;
        StartTransport();
        await Next<TransportConnected>();

        await Transport.SendAsync(new CreateRoom("test room"), default);

        await WaitForReceived(lines => lines.Any(l => l.Line == "PRIVMSG BanchoBot :!mp make test room"));
        if (createdReplyFirst)
        {
            Assert.Equal("test room", (await Next<RoomCreated>()).Title);
            Assert.Equal(_server.LastRoomId, (await Next<RoomJoined>()).RoomId);
        }
        else
        {
            Assert.Equal(_server.LastRoomId, (await Next<RoomJoined>()).RoomId);
            Assert.Equal("test room", (await Next<RoomCreated>()).Title);
        }
    }

    [Fact]
    public async Task Reports_a_room_bancho_refuses_to_make()
    {
        _server.RoomLimitReached = true;
        StartTransport();
        await Next<TransportConnected>();

        await Transport.SendAsync(new CreateRoom("test room"), default);

        Assert.Equal(BanchoSimulator.RoomLimitText, (await Next<RoomCreateRefused>()).Reason);
    }

    [Fact]
    public async Task Joins_an_existing_room()
    {
        _server.AddRoom(555);
        StartTransport();
        await Next<TransportConnected>();

        await Transport.SendAsync(new JoinRoom(555), default);

        Assert.Equal(555, (await Next<RoomJoined>()).RoomId);
    }

    [Fact]
    public async Task Reports_a_room_that_no_longer_exists()
    {
        StartTransport();
        await Next<TransportConnected>();

        await Transport.SendAsync(new JoinRoom(404), default);

        var failure = await Next<RoomJoinFailed>();
        Assert.Equal(404, failure.RoomId);
        Assert.Contains("No such channel", failure.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Room_state_arrives_even_when_the_room_is_empty()
    {
        await ConnectAndCreateRoom();

        await Transport.SendAsync(new RequestRoomState(), default);

        Assert.Empty((await Next<RoomStateReceived>()).State.Players);
    }

    [Fact]
    public async Task Room_state_includes_players()
    {
        await ConnectAndCreateRoom();
        _server.AddPlayer("some one", 42);
        Assert.Equal(new PlayerJoined("some one", 1, Team.None), await Next<PlayerJoined>());

        await Transport.SendAsync(new RequestRoomState(), default);

        var player = Assert.Single((await Next<RoomStateReceived>()).State.Players);
        Assert.Equal("some one", player.Username);
        Assert.Equal(42, player.OsuUserId);
    }

    [Fact]
    public async Task Sends_host_transfer_and_reports_the_change()
    {
        await ConnectAndCreateRoom();

        await Transport.SendAsync(new TransferHost("player"), default);

        Assert.Equal("player", (await Next<HostChanged>()).Username);
    }

    [Fact]
    public async Task Stats_requested_privately_are_marked_private()
    {
        await ConnectAndCreateRoom();

        await Transport.SendAsync(new RequestStat("player", ViaPrivateMessage: true), default);

        var stat = await Next<PlayerStatReceived>();
        Assert.True(stat.ViaPrivateMessage);
        Assert.Equal(PlayerActivity.Afk, stat.Stat.Activity);
    }

    [Fact]
    public async Task Player_chat_is_delivered_with_actions_marked()
    {
        await ConnectAndCreateRoom();

        _server.Chat("player", _server.LastChannel, "!skip");
        _server.Chat("player", _server.LastChannel, "\u0001ACTION waves\u0001");

        Assert.Equal(new ChatReceived("player", "!skip", false), await Next<ChatReceived>());
        Assert.Equal(new ChatReceived("player", "waves", true), await Next<ChatReceived>());
    }

    [Fact]
    public async Task Private_messages_are_delivered()
    {
        StartTransport();
        await Next<TransportConnected>();

        _server.Chat("player", "creator", "hello");

        Assert.Equal(new PrivateMessageReceived("player", "hello"), await Next<PrivateMessageReceived>());
    }

    [Fact]
    public async Task A_player_cannot_impersonate_BanchoBot_through_chat()
    {
        await ConnectAndCreateRoom();

        _server.Chat("player", _server.LastChannel, "someone became the host.");

        Assert.Equal("someone became the host.", (await Next<ChatReceived>()).Text);
        await AssertNoEvent<HostChanged>(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public async Task Ignores_messages_from_other_channels()
    {
        await ConnectAndCreateRoom();

        _server.FromBot("#mp_99999", "stranger joined in slot 1.");
        _server.AddPlayer("real", 1);

        Assert.Equal("real", (await Next<PlayerJoined>()).Username);
    }

    [Fact]
    public async Task Reports_when_the_room_closes()
    {
        var roomId = await ConnectAndCreateRoom();

        await Transport.SendAsync(new CloseRoom(), default);

        await Next<RoomCloseAcknowledged>();
        Assert.Equal(roomId, (await Next<RoomClosed>()).RoomId);
    }

    [Fact]
    public async Task One_malformed_line_does_not_end_the_session()
    {
        await ConnectAndCreateRoom();

        _server.Send(":");
        _server.Send("");
        _server.FromBot(_server.LastChannel, "player joined in slot 99999999999999.");
        _server.AddPlayer("after", 1);

        Assert.Equal("after", (await Next<PlayerJoined>()).Username);
        Assert.Equal(1, _server.Connections);
    }

    [Fact]
    public async Task Reconnects_after_a_lost_connection_and_can_rejoin()
    {
        var roomId = await ConnectAndCreateRoom();

        _server.DropClient();

        await Next<TransportDisconnected>();
        await Next<TransportConnected>();
        Assert.Equal(2, _server.Connections);

        await Transport.SendAsync(new JoinRoom(roomId), default);
        Assert.Equal(roomId, (await Next<RoomJoined>()).RoomId);
    }

    [Fact]
    public async Task Room_commands_are_dropped_until_the_room_is_rejoined()
    {
        await ConnectAndCreateRoom();
        _server.DropClient();
        await Next<TransportDisconnected>();
        await Next<TransportConnected>();
        var before = _server.Received.Count;

        await Transport.SendAsync(new StartMatch(), default);
        await Task.Delay(100);

        Assert.Equal(before, _server.Received.Count);
    }

    [Fact]
    public async Task Sends_a_ping_when_idle_and_stays_connected_if_answered()
    {
        StartTransport(idlePingAfter: TimeSpan.FromMilliseconds(40), deadAfter: TimeSpan.FromMilliseconds(400));
        await Next<TransportConnected>();

        await WaitForReceived(lines => lines.Count(l => l.Line.StartsWith("PING", StringComparison.Ordinal)) >= 3);

        await AssertNoEvent<TransportDisconnected>(TimeSpan.FromMilliseconds(600));
        Assert.Equal(1, _server.Connections);
    }

    [Fact]
    public async Task Reconnects_when_the_server_goes_silent()
    {
        _server.AnswerPings = false;
        StartTransport(idlePingAfter: TimeSpan.FromMilliseconds(30), deadAfter: TimeSpan.FromMilliseconds(120));
        await Next<TransportConnected>();

        var disconnected = await Next<TransportDisconnected>();

        Assert.Contains("silent", disconnected.Reason, StringComparison.Ordinal);
        await Next<TransportConnected>();
    }

    [Fact]
    public async Task The_rate_limit_holds_on_the_wire()
    {
        var window = TimeSpan.FromMilliseconds(400);
        StartTransport(messagesPerWindow: 3, rateWindow: window);
        await Next<TransportConnected>();
        _server.AddRoom(777);
        await Transport.SendAsync(new JoinRoom(777), default);
        await Next<RoomJoined>();
        await Task.Delay(window);

        for (var i = 0; i < 10; i++)
        {
            await Transport.SendAsync(new Say($"message {i}", CommandPriority.Admin), default);
        }

        await WaitForReceived(lines => lines.Count(l => l.Line.Contains(":message ", StringComparison.Ordinal)) == 10);

        // Arrival times can bunch up slightly compared with send times, hence the small tolerance.
        var arrivals = _server.Received.Where(l => l.Line.Contains(":message ", StringComparison.Ordinal)).Select(l => l.At).ToList();
        for (var i = 3; i < arrivals.Count; i++)
        {
            Assert.True(arrivals[i] - arrivals[i - 3] >= window - TimeSpan.FromMilliseconds(100), $"Message {i} broke the limit.");
        }

        Assert.Equal(Enumerable.Range(0, 10).Select(i => $"PRIVMSG :message {i}"), _server.Received.Where(l => l.Line.Contains(":message ", StringComparison.Ordinal)).Select(l => "PRIVMSG " + l.Line[l.Line.IndexOf(':', StringComparison.Ordinal)..]));
    }

    [Fact]
    public async Task Commands_before_connecting_are_dropped()
    {
        _server.MotdGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        StartTransport();
        await WaitForReceived(lines => lines.Any(l => l.Line.StartsWith("USER", StringComparison.Ordinal)));

        await Transport.SendAsync(new CreateRoom("too early"), default);
        _server.MotdGate.SetResult();
        await Next<TransportConnected>();
        await Task.Delay(100);

        Assert.DoesNotContain(_server.Received, l => l.Line.Contains("too early", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stopping_ends_the_event_stream()
    {
        StartTransport();
        await Next<TransportConnected>();

        await _stop.CancelAsync();
        await _collector!;

        Assert.True(_events.Reader.Completion.IsCompleted || await DrainsToCompletion());
    }

    private async Task<bool> DrainsToCompletion()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        await foreach (var _ in _events.Reader.ReadAllAsync(timeout.Token))
        {
        }

        return true;
    }
}
