using Microsoft.Extensions.Time.Testing;
using OsuRotate.Application;
using OsuRotate.Domain;

namespace OsuRotate.Application.Tests;

// Phase 5, issue 06: rule edits reach the room, the store, and the next start.
public sealed class SettingsChangeTests : IAsyncDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeTransport _transport = new();
    private readonly InMemoryLobbyStore _store = new();
    private readonly CancellationTokenSource _stop = new();
    private LobbyActor? _actor;
    private Task? _run;

    private static LobbySettings Configured => new() { OwnerName = "creator", HostAfkCheckInterval = TimeSpan.Zero, BotVersion = "1.2.3" };

    private LobbyActor Start(LobbyLifecycle? lifecycle = null)
    {
        _actor = new LobbyActor(new LobbyEngine(Configured, _time), _transport, new FakeCatalog(), _time, lifecycle: lifecycle, store: _store);
        _run = _actor.RunAsync(_stop.Token);
        return _actor;
    }

    private async Task<LobbyActor> OpenAsync()
    {
        var actor = Start(new LobbyLifecycle(1, "room"));
        _transport.Emit(new TransportConnected());
        _transport.Emit(new RoomJoined(1));
        _transport.Emit(new RoomStateReceived(new RoomState("room", 1, 0, string.Empty, "HeadToHead", "Score", string.Empty,
            [new RoomPlayer(1, "Not Ready", 11, "p1", true, Team.None, "Host")])));
        await WaitUntilAsync(() => actor.Snapshot.Status == LobbyStatus.Open);
        _transport.ClearSent();
        return actor;
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
    public async Task A_new_keep_is_put_right_in_an_open_room()
    {
        var actor = await OpenAsync();
        await actor.PostAsync(new ChangeSettings(actor.Snapshot.Settings with { Keeping = new RoomKeeping { Size = 8 } }));
        await _transport.WaitForAsync(sent => sent.Contains(new SetSize(8)));
    }

    [Fact]
    public async Task Without_a_room_the_change_is_kept_but_nothing_is_sent()
    {
        var actor = Start();
        await actor.PostAsync(new ChangeSettings(actor.Snapshot.Settings with { Keeping = new RoomKeeping { Size = 8 }, Regulation = actor.Snapshot.Settings.Regulation with { StarMax = 5 } }));
        await actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(8, actor.Snapshot.Settings.Keeping.Size);
        Assert.Empty(_transport.Sent);
    }

    [Fact]
    public async Task Changes_are_saved_and_a_restart_keeps_them()
    {
        var actor = await OpenAsync();
        var edited = actor.Snapshot.Settings with { AfkKickThreshold = 9, InfoMessage = "hello" };
        await actor.PostAsync(new ChangeSettings(edited));
        await WaitUntilAsync(() => _store.Saved.Settings?.AfkKickThreshold == 9);

        var recovered = await LobbyRecovery.RecoverAsync(_store, NoMatchHistory.Instance, Configured with { BotVersion = "2.0.0" }, _time, CancellationToken.None);
        Assert.Equal(edited with { BotVersion = "2.0.0" }, recovered.Engine.Settings);
    }

    [Fact]
    public async Task An_invalid_change_is_neither_applied_nor_saved()
    {
        var actor = await OpenAsync();
        var before = actor.Snapshot.Settings;
        await actor.PostAsync(new ChangeSettings(before with { AfkKickThreshold = 0 }));
        await actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(before, actor.Snapshot.Settings);
        Assert.Equal(before, _store.Saved.Settings);
    }
}
