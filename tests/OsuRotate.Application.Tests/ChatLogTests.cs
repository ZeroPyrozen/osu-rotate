using Microsoft.Extensions.Time.Testing;
using OsuRotate.Application;
using OsuRotate.Domain;

namespace OsuRotate.Application.Tests;

// Phase 5, issue 05: the lobby's chat for the dashboard, kept in memory only.
public sealed class ChatLogTests : IAsyncDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeTransport _transport = new();
    private readonly CancellationTokenSource _stop = new();
    private LobbyActor? _actor;
    private Task? _run;

    private static ChatLine Line(int i) => new(DateTimeOffset.UnixEpoch.AddSeconds(i), $"p{i}", $"line {i}", ChatSource.Player);

    [Fact]
    public void Keeps_the_latest_lines_in_order_up_to_its_capacity()
    {
        var log = new ChatLog(capacity: 3);
        for (var i = 1; i <= 5; i++)
        {
            log.Add(Line(i));
        }

        Assert.Equal(["line 3", "line 4", "line 5"], log.Lines.Select(l => l.Text));
    }

    [Fact]
    public void Tells_listeners_about_each_new_line()
    {
        var log = new ChatLog();
        var seen = new List<ChatLine>();
        log.Added += seen.Add;
        log.Add(Line(1));
        log.Add(Line(2));
        Assert.Equal([Line(1), Line(2)], seen);
    }

    [Fact]
    public void The_default_capacity_is_a_few_hundred_lines()
    {
        Assert.Equal(300, new ChatLog().Capacity);
    }

    private async Task<LobbyActor> OpenAsync()
    {
        var engine = new LobbyEngine(new LobbySettings { OwnerName = "creator", HostAfkCheckInterval = TimeSpan.Zero }, _time);
        _actor = new LobbyActor(engine, _transport, new FakeCatalog(), _time, lifecycle: new LobbyLifecycle(1, "room"));
        _run = _actor.RunAsync(_stop.Token);
        _transport.Emit(new TransportConnected());
        _transport.Emit(new RoomJoined(1));
        _transport.Emit(new RoomStateReceived(new RoomState("room", 1, 0, string.Empty, "HeadToHead", "Score", string.Empty,
            [new RoomPlayer(1, "Not Ready", 11, "p1", true, Team.None, "Host")])));
        await WaitUntilAsync(() => _actor.Snapshot.Status == LobbyStatus.Open);
        return _actor;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_run is not null)
        {
            await _run;
        }

        _stop.Dispose();
    }

    [Fact]
    public async Task Players_chat_is_logged_as_it_arrives()
    {
        var actor = await OpenAsync();
        _transport.Emit(new ChatReceived("p1", "hello <b>all</b>", false));
        _transport.Emit(new ChatReceived("p1", "waves", true));
        await WaitUntilAsync(() => actor.Chat.Lines.Count == 2);

        Assert.Equal(new ChatLine(_time.GetUtcNow(), "p1", "hello <b>all</b>", ChatSource.Player), actor.Chat.Lines[0]);
        Assert.True(actor.Chat.Lines[1].IsAction);
    }

    [Fact]
    public async Task The_bots_announcements_are_logged_one_line_each()
    {
        var actor = await OpenAsync();
        // The default info message is three lines.
        _transport.Emit(new ChatReceived("p1", "!info", false));
        await WaitUntilAsync(() => actor.Chat.Lines.Count(l => l.Source == ChatSource.Bot) == 3);

        var bot = actor.Chat.Lines.Where(l => l.Source == ChatSource.Bot).ToList();
        Assert.StartsWith("- osu-rotate", bot[0].Text, StringComparison.Ordinal);
        Assert.All(bot, l => Assert.Equal("creator", l.From));
        Assert.DoesNotContain(bot, l => l.Text.Contains('\n', StringComparison.Ordinal));
    }

    [Fact]
    public async Task What_the_owner_sends_is_logged_and_sent_ahead_of_announcements()
    {
        var actor = await OpenAsync();
        await actor.PostAsync(new SayInLobby("welcome"));
        await _transport.WaitForAsync(sent => sent.Contains(new Say("welcome", CommandPriority.Admin)));

        Assert.Contains(new ChatLine(_time.GetUtcNow(), "creator", "welcome", ChatSource.Owner), actor.Chat.Lines);
    }

    [Fact]
    public async Task A_kick_goes_straight_to_bancho()
    {
        var actor = await OpenAsync();
        await actor.PostAsync(new KickFromLobby("p1"));
        await _transport.WaitForAsync(sent => sent.Contains(new KickPlayer("p1")));
    }

    [Fact]
    public async Task Nothing_is_logged_when_there_is_no_room()
    {
        var engine = new LobbyEngine(new LobbySettings { OwnerName = "creator", HostAfkCheckInterval = TimeSpan.Zero }, _time);
        _actor = new LobbyActor(engine, _transport, new FakeCatalog(), _time);
        _run = _actor.RunAsync(_stop.Token);
        await _actor.PostAsync(new CreateLobby("room"));
        await _actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));

        // Ordered on the transport: once CreateRoom is out, the chat before it was handled.
        _transport.Emit(new ChatReceived("someone", "hi", false));
        _transport.Emit(new TransportConnected());
        await _transport.WaitForAsync(sent => sent.Any(c => c is CreateRoom));
        Assert.Empty(_actor.Chat.Lines);
    }
}
