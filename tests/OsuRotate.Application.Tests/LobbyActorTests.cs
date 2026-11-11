using Microsoft.Extensions.Time.Testing;
using OsuRotate.Application;
using OsuRotate.Domain;

namespace OsuRotate.Application.Tests;

public sealed class LobbyActorTests : IAsyncDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly FakeTransport _transport = new();
    private readonly FakeCatalog _catalog = new();
    private readonly RecordingDiagnostics _diagnostics = new();
    private readonly CancellationTokenSource _stop = new();
    private LobbyActor? _actor;
    private Task? _run;

    private LobbyActor Start(Func<LobbySettings, LobbySettings>? configure = null)
    {
        var settings = new LobbySettings { OwnerName = "creator", HostAfkCheckInterval = TimeSpan.Zero };
        var engine = new LobbyEngine(configure?.Invoke(settings) ?? settings, _time);
        _actor = new LobbyActor(engine, _transport, _catalog, _time, _diagnostics);
        _run = _actor.RunAsync(_stop.Token);
        return _actor;
    }

    private async Task<LobbyActor> StartWithPlayersAsync(Func<LobbySettings, LobbySettings>? configure = null)
    {
        var actor = Start(configure);
        foreach (var (name, slot) in new[] { ("p1", 1), ("p2", 2), ("p3", 3) })
        {
            _transport.Emit(new PlayerJoined(name, slot, Team.None));
        }

        _transport.Emit(new HostChanged("p1"));
        await WaitUntilAsync(() => actor.Snapshot.Host == "p1");
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
    public async Task Bancho_events_reach_the_engine_and_its_commands_go_out()
    {
        var actor = Start();
        _transport.Emit(new PlayerJoined("p1", 1, Team.None));

        await _transport.WaitForAsync(sent => sent.Contains(new TransferHost("p1")));
        await WaitUntilAsync(() => actor.Snapshot.Players.Count == 1);
        Assert.Equal("p1", Assert.Single(actor.Snapshot.Queue));
    }

    [Fact]
    public async Task Owner_actions_run_on_the_engine()
    {
        var actor = await StartWithPlayersAsync();
        await actor.PostAsync(new SkipHost());
        await _transport.WaitForAsync(sent => sent.Contains(new TransferHost("p2")));

        await actor.PostAsync(new DenyPlayer("p3"));
        await actor.PostAsync(new ReorderQueue("p1, p2"));
        await actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(["p3"], actor.Snapshot.Denied);
        Assert.DoesNotContain("p3", actor.Snapshot.Queue);

        await actor.PostAsync(new AllowPlayer("p3"));
        await actor.PostAsync(new SkipToPlayer("p3"));
        await _transport.WaitForAsync(sent => sent.Contains(new TransferHost("p3")));
    }

    [Fact]
    public async Task Settings_changes_show_in_the_snapshot()
    {
        var actor = Start();
        var settings = actor.Snapshot.Settings with { AfkKickThreshold = 9 };
        await actor.PostAsync(new ChangeSettings(settings));
        await actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(9, actor.Snapshot.Settings.AfkKickThreshold);
    }

    [Fact]
    public async Task The_owner_speaks_ahead_of_announcements()
    {
        var actor = Start();
        await actor.PostAsync(new SayInLobby("hello"));
        var sent = await _transport.WaitForAsync(s => s.Count == 1);
        Assert.Equal(new Say("hello", CommandPriority.Admin), sent[0]);
    }

    [Fact]
    public async Task Timers_fire_with_no_event_to_wake_the_actor()
    {
        var actor = await StartWithPlayersAsync();
        await actor.PostAsync(new SkipHost());
        await _transport.WaitForAsync(sent => sent.Contains(new TransferHost("p2")));

        // Bancho never confirms the transfer, so after the timeout the bot asks for the room state.
        _time.Advance(TimeSpan.FromSeconds(4));
        await actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.DoesNotContain(_transport.Sent, c => c is RequestRoomState);

        _time.Advance(TimeSpan.FromSeconds(1));
        await _transport.WaitForAsync(sent => sent.Any(c => c is RequestRoomState));
    }

    [Fact]
    public async Task Lookups_go_to_the_catalog_and_the_answer_comes_back()
    {
        await StartWithPlayersAsync();
        _transport.Emit(new BeatmapChanged(100, "title 100", false));

        var sent = await _transport.WaitForAsync(s => s.OfType<SetBeatmap>().Any());
        Assert.Equal(100, Assert.Single(sent.OfType<SetBeatmap>()).BeatmapId);
        Assert.Equal([100], _catalog.Asked);
        Assert.DoesNotContain(sent, c => c is LookUpBeatmap);
    }

    [Fact]
    public async Task A_slow_lookup_does_not_hold_up_the_lobby()
    {
        var actor = await StartWithPlayersAsync();
        _catalog.Hold = true;
        _transport.Emit(new BeatmapChanged(100, "title 100", false));
        await WaitUntilAsync(() => _catalog.Asked.Contains(100));

        _transport.Emit(new ChatReceived("p2", "!q", false));
        await _transport.WaitForAsync(sent => sent.OfType<Say>().Any());
        Assert.DoesNotContain(_transport.Sent, c => c is SetBeatmap);

        _catalog.Answer(100);
        await _transport.WaitForAsync(sent => sent.OfType<SetBeatmap>().Any());
        Assert.Equal(100, actor.Snapshot.BeatmapId);
    }

    [Fact]
    public async Task A_throwing_catalog_lets_the_map_through_and_is_reported()
    {
        var actor = await StartWithPlayersAsync();
        _catalog.Throw = true;
        _transport.Emit(new BeatmapChanged(100, "title 100", false));
        await WaitUntilAsync(() => _diagnostics.Reports.Contains("lookup 100"));
        await actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.DoesNotContain(_transport.Sent, c => c is SetBeatmap);
        Assert.Equal(1, actor.Snapshot.Players.Count(p => p.Name == "p1"));
    }

    [Fact]
    public async Task A_failed_send_is_reported_and_the_rest_still_go()
    {
        var actor = await StartWithPlayersAsync();
        _transport.Failing = typeof(TransferHost);
        await actor.PostAsync(new SkipHost());
        await actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("send TransferHost", _diagnostics.Reports);

        _transport.Failing = null;
        _transport.Emit(new ChatReceived("p2", "!q", false));
        await _transport.WaitForAsync(sent => sent.OfType<Say>().Any());
    }

    [Fact]
    public async Task One_bad_input_does_not_stop_the_loop()
    {
        var actor = Start();
        await actor.PostAsync(new UnknownInput());
        _transport.Emit(new PlayerJoined("p1", 1, Team.None));

        await _transport.WaitForAsync(sent => sent.Contains(new TransferHost("p1")));
        Assert.Contains("input UnknownInput: ArgumentOutOfRangeException", _diagnostics.Reports);
    }

    [Fact]
    public async Task Snapshots_are_announced_only_when_they_change()
    {
        var actor = Start();
        var seen = new List<LobbySnapshot>();
        actor.SnapshotChanged += seen.Add;

        _transport.Emit(new PlayerJoined("p1", 1, Team.None));
        await WaitUntilAsync(() => seen.Count == 1);
        _transport.Emit(new ChatReceived("p1", "hello", false));
        await actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Single(seen);
        Assert.Equal("p1", seen[0].Players[0].Name);
    }

    [Fact]
    public async Task A_change_only_to_players_or_the_deny_list_is_announced()
    {
        var actor = await StartWithPlayersAsync();
        var seen = new List<LobbySnapshot>();
        actor.SnapshotChanged += seen.Add;

        _transport.Emit(new PlayerMoved("p3", 7));
        await WaitUntilAsync(() => seen.Count == 1);
        Assert.Equal(7, seen[0].Players.Single(p => p.Name == "p3").Slot);

        await actor.PostAsync(new DenyPlayer("someone outside"));
        await WaitUntilAsync(() => seen.Count == 2);
        Assert.Equal(["someone_outside"], seen[1].Denied);
    }

    [Fact]
    public async Task A_clean_end_of_the_event_stream_is_reported()
    {
        Start();
        _transport.End();
        await WaitUntilAsync(() => _diagnostics.Reports.Contains("ended cleanly"));
    }

    [Fact]
    public async Task The_end_of_the_event_stream_is_reported()
    {
        var actor = Start();
        _transport.End(new IOException("gone"));
        await WaitUntilAsync(() => _diagnostics.Reports.Contains("ended gone"));

        // Owner actions and timers keep working.
        await actor.PostAsync(new SayInLobby("still here"));
        await _transport.WaitForAsync(sent => sent.Count == 1);
    }

    [Fact]
    public async Task Stopping_ends_the_run()
    {
        Start();
        await _stop.CancelAsync();
        await _run!.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed record UnknownInput : LobbyInput;
}
