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
    private readonly RecordingStore _store;
    private readonly RecordingArchive _archive = new();
    private readonly ScriptedHistory _history = new();
    private readonly CancellationTokenSource _stop = new();
    private LobbyActor? _actor;

    public LobbyActorTests() => _store = new RecordingStore(_transport);
    private Task? _run;

    private LobbyActor Start(Func<LobbySettings, LobbySettings>? configure = null)
    {
        var settings = new LobbySettings { OwnerName = "creator", HostAfkCheckInterval = TimeSpan.Zero };
        var engine = new LobbyEngine(configure?.Invoke(settings) ?? settings, _time);
        _actor = new LobbyActor(engine, _transport, _catalog, _time, _diagnostics, store: _store, archive: _archive, history: _history);
        _run = _actor.RunAsync(_stop.Token);
        return _actor;
    }

    // Connected and in an empty room 1, with the commands that took sent and cleared.
    private async Task<LobbyActor> StartInRoomAsync(Func<LobbySettings, LobbySettings>? configure = null)
    {
        var actor = Start(configure);
        _transport.Emit(new TransportConnected());
        await actor.PostAsync(new AttachLobby(1));
        await actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        _transport.Emit(new RoomJoined(1));
        _transport.Emit(new RoomStateReceived(new RoomState("room", 1, 0, string.Empty, "HeadToHead", "Score", string.Empty, [])));
        await WaitUntilAsync(() => actor.Snapshot.Status == LobbyStatus.Open);
        _transport.ClearSent();
        return actor;
    }

    private async Task<LobbyActor> StartWithPlayersAsync(Func<LobbySettings, LobbySettings>? configure = null)
    {
        var actor = await StartInRoomAsync(configure);
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
        var actor = await StartInRoomAsync();
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
        var actor = await StartInRoomAsync();
        var settings = actor.Snapshot.Settings with { AfkKickThreshold = 9 };
        await actor.PostAsync(new ChangeSettings(settings));
        await actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(9, actor.Snapshot.Settings.AfkKickThreshold);
    }

    [Fact]
    public async Task The_owner_speaks_ahead_of_announcements()
    {
        var actor = await StartInRoomAsync();
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
        var actor = await StartInRoomAsync();
        await actor.PostAsync(new UnknownInput());
        _transport.Emit(new PlayerJoined("p1", 1, Team.None));

        await _transport.WaitForAsync(sent => sent.Contains(new TransferHost("p1")));
        Assert.Contains("input UnknownInput: ArgumentOutOfRangeException", _diagnostics.Reports);
    }

    [Fact]
    public async Task Snapshots_are_announced_only_when_they_change()
    {
        var actor = await StartInRoomAsync();
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
        var actor = await StartInRoomAsync();
        _transport.End(new IOException("gone"));
        await WaitUntilAsync(() => _diagnostics.Reports.Contains("ended gone"));

        // Owner actions and timers keep working.
        await actor.PostAsync(new SayInLobby("still here"));
        await _transport.WaitForAsync(sent => sent.Count == 1);
    }

    [Fact]
    public async Task Events_before_there_is_a_room_do_not_reach_the_engine()
    {
        var actor = Start();
        await actor.PostAsync(new CreateLobby("my room"));
        await actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));

        // Transport events arrive in order, so once the connection's CreateRoom is out the join was handled.
        _transport.Emit(new PlayerJoined("p1", 1, Team.None));
        _transport.Emit(new TransportConnected());
        var sent = await _transport.WaitForAsync(s => s.Any(c => c is CreateRoom));

        Assert.Empty(actor.Snapshot.Players);
        Assert.Equal([new CreateRoom("my room")], sent);
    }

    [Fact]
    public async Task A_closed_room_starts_the_engine_over_with_the_same_settings()
    {
        var actor = await StartWithPlayersAsync(s => s with { AfkKickThreshold = 9 });
        _transport.Emit(new RoomCloseAcknowledged());
        await WaitUntilAsync(() => actor.Snapshot.Status == LobbyStatus.Idle);

        Assert.Empty(actor.Snapshot.Players);
        Assert.Empty(actor.Snapshot.Queue);
        Assert.Null(actor.Snapshot.Host);
        Assert.Equal(9, actor.Snapshot.Settings.AfkKickThreshold);
    }

    [Fact]
    public async Task Close_when_empty_closes_after_the_last_player_leaves()
    {
        var actor = await StartWithPlayersAsync();
        await actor.PostAsync(new CloseLobbyWhenEmpty(true));
        _transport.Emit(new PlayerLeft("p1"));
        _transport.Emit(new PlayerLeft("p2"));
        await actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.DoesNotContain(_transport.Sent, c => c is CloseRoom);

        _transport.Emit(new PlayerLeft("p3"));
        await _transport.WaitForAsync(sent => sent.Any(c => c is CloseRoom));
        Assert.Equal(LobbyStatus.Closing, actor.Snapshot.Status);
    }

    [Fact]
    public async Task The_queue_is_saved_before_the_commands_that_follow_are_sent()
    {
        var actor = await StartInRoomAsync();
        _transport.Emit(new PlayerJoined("p1", 1, Team.None));
        await _transport.WaitForAsync(sent => sent.Contains(new TransferHost("p1")));

        var save = Assert.Single(_store.Saves, s => s.Lobby.Session?.Queue.SequenceEqual(["p1"]) == true);
        Assert.Equal(0, save.SentBefore);
        Assert.Equal(1, save.Lobby.Session!.RoomId);
        await actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Nothing_is_saved_when_nothing_that_survives_a_restart_changed()
    {
        var actor = await StartWithPlayersAsync();
        var before = _store.Saves.Count;
        _transport.Emit(new ChatReceived("p2", "hello", false));
        _transport.Emit(new ChatReceived("p2", "!q", false));
        await _transport.WaitForAsync(sent => sent.OfType<Say>().Any());
        await actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(before, _store.Saves.Count);
    }

    [Fact]
    public async Task Host_deny_list_and_settings_changes_are_saved()
    {
        var actor = await StartWithPlayersAsync();
        Assert.Equal("p1", _store.Last!.Session!.Host);

        await actor.PostAsync(new DenyPlayer("p3"));
        await actor.PostAsync(new ChangeSettings(actor.Snapshot.Settings with { AfkKickThreshold = 2 }));
        await actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(["p3"], _store.Last!.Denied);
        Assert.Equal(["p1", "p2"], _store.Last.Session!.Queue);
        Assert.Equal(2, _store.Last.Settings!.AfkKickThreshold);
    }

    [Fact]
    public async Task Closing_saves_that_there_is_no_room()
    {
        var actor = await StartWithPlayersAsync();
        _transport.Emit(new RoomCloseAcknowledged());
        await WaitUntilAsync(() => actor.Snapshot.Status == LobbyStatus.Idle);
        await actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(_store.Last!.Session);
    }

    [Fact]
    public async Task A_failed_save_is_reported_the_lobby_goes_on_and_the_next_change_saves()
    {
        var actor = await StartInRoomAsync();
        _store.Fail = true;
        _transport.Emit(new PlayerJoined("p1", 1, Team.None));
        await _transport.WaitForAsync(sent => sent.Contains(new TransferHost("p1")));
        Assert.Contains("save disk full", _diagnostics.Reports);

        _store.Fail = false;
        _transport.Emit(new HostChanged("p1"));
        await WaitUntilAsync(() => _store.Last?.Session?.Host == "p1");
        Assert.Equal(["p1"], _store.Last!.Session!.Queue);
        await actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task A_failed_save_is_tried_again_even_if_the_next_input_changes_nothing()
    {
        var actor = await StartInRoomAsync();
        _store.Fail = true;
        _transport.Emit(new PlayerJoined("p1", 1, Team.None));
        await _transport.WaitForAsync(sent => sent.Contains(new TransferHost("p1")));

        _store.Fail = false;
        _transport.Emit(new ChatReceived("p1", "hello", false));
        await WaitUntilAsync(() => _store.Last?.Session?.Queue.Count == 1);
        await actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task A_finished_match_goes_to_the_archive_and_not_to_bancho()
    {
        var actor = await StartWithPlayersAsync();
        _transport.Emit(new MatchStarted());
        _transport.Emit(new PlayerFinished("p1", 1000, true));
        _transport.Emit(new MatchFinished());
        await WaitUntilAsync(() => !_archive.Saved.IsEmpty);
        await actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));

        var (roomId, match) = Assert.Single(_archive.Saved);
        Assert.Equal(1, roomId);
        Assert.Equal("p1", match.HostName);
        Assert.Equal([new MatchRecordScore("p1", null, 1000, true)], match.Scores);
        Assert.DoesNotContain(_transport.Sent, c => c is RecordMatch);
    }

    [Fact]
    public async Task A_match_that_could_not_be_saved_is_saved_with_the_next_input()
    {
        var actor = await StartWithPlayersAsync();
        _archive.Fail = true;
        _transport.Emit(new MatchStarted());
        _transport.Emit(new MatchAborted());
        await WaitUntilAsync(() => _diagnostics.Reports.Contains("save archive down"));
        Assert.Empty(_archive.Saved);

        _archive.Fail = false;
        _transport.Emit(new ChatReceived("p1", "hello", false));
        await WaitUntilAsync(() => _archive.Saved.Count == 1);
        Assert.Equal(MatchOutcome.Aborted, _archive.Saved.Single().Match.Outcome);
        await actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Without_an_archive_matches_are_dropped_and_never_sent()
    {
        var engine = new LobbyEngine(new LobbySettings { OwnerName = "creator", HostAfkCheckInterval = TimeSpan.Zero }, _time);
        _actor = new LobbyActor(engine, _transport, _catalog, _time, _diagnostics);
        _run = _actor.RunAsync(_stop.Token);
        _transport.Emit(new TransportConnected());
        await _actor.PostAsync(new AttachLobby(1));
        _transport.Emit(new RoomJoined(1));
        _transport.Emit(new RoomStateReceived(new RoomState("room", 1, 0, string.Empty, "HeadToHead", "Score", string.Empty, [])));
        _transport.Emit(new PlayerJoined("p1", 1, Team.None));
        _transport.Emit(new MatchStarted());
        _transport.Emit(new MatchFinished());
        _transport.Emit(new ChatReceived("p1", "!q", false));
        await _transport.WaitForAsync(sent => sent.OfType<Say>().Any());

        Assert.DoesNotContain(_transport.Sent, c => c is RecordMatch);
    }

    [Fact]
    public async Task Nothing_is_saved_until_the_saved_queue_has_been_restored()
    {
        var engine = new LobbyEngine(new LobbySettings { OwnerName = "creator", HostAfkCheckInterval = TimeSpan.Zero }, _time);
        engine.RestoreOnNextRoomState(["p2", "p1"], "p1", runningMatch: null);
        _actor = new LobbyActor(engine, _transport, _catalog, _time, _diagnostics, new LobbyLifecycle(1, "room"), _store, _archive);
        _run = _actor.RunAsync(_stop.Token);

        _transport.Emit(new TransportConnected());
        _transport.Emit(new RoomJoined(1));
        _transport.Emit(new PlayerJoined("p3", 3, Team.None));
        _transport.Emit(new ChatReceived("p3", "hi", false));
        await _transport.WaitForAsync(sent => sent.Any(c => c is RequestRoomState));
        await _actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(_store.Saves);

        _transport.Emit(new RoomStateReceived(new RoomState("room", 1, 0, string.Empty, "HeadToHead", "Score", string.Empty,
            [new RoomPlayer(1, "Not Ready", 11, "p1", true, Team.None, "Host"), new RoomPlayer(2, "Not Ready", 22, "p2", false, Team.None, string.Empty),
             new RoomPlayer(3, "Not Ready", 33, "p3", false, Team.None, string.Empty)])));
        await WaitUntilAsync(() => _store.Last?.Session?.Queue.Count == 3);
        Assert.Equal(["p2", "p1", "p3"], _store.Last!.Session!.Queue);
        await _actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task The_match_history_is_read_for_the_room_and_kicks_come_back_to_the_engine()
    {
        var actor = await StartWithPlayersAsync(s => s with { HostKickTolerance = 1 });
        await WaitUntilAsync(() => _history.Asked.Contains((1, null)));

        // A kick by the host after the bot joined: the counterattack kicks p1.
        _history.Latest = 101;
        _history.Kicks.Add((33, "p3", _time.GetUtcNow()));
        _time.Advance(TimeSpan.FromSeconds(60));
        await _transport.WaitForAsync(sent => sent.Contains(new KickPlayer("p1")));
        Assert.Contains((1L, (long?)100), _history.Asked);
        Assert.DoesNotContain(_transport.Sent, c => c is ReadMatchHistory);
        await actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Players_who_left_on_their_own_are_not_counted_as_kicked()
    {
        var actor = await StartWithPlayersAsync(s => s with { HostKickTolerance = 1 });
        await WaitUntilAsync(() => _history.Asked.Contains((1, null)));

        _history.Latest = 101;
        _history.Leaves.Add((33, "p3", _time.GetUtcNow()));
        _time.Advance(TimeSpan.FromSeconds(60));
        await WaitUntilAsync(() => _history.Asked.Contains((1, 100)));
        await actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.DoesNotContain(_transport.Sent, c => c is KickPlayer);
    }

    [Fact]
    public async Task A_failing_history_is_ignored_and_read_again_later()
    {
        _history.Throw = true;
        var actor = await StartWithPlayersAsync(s => s with { HostKickTolerance = 1 });
        await WaitUntilAsync(() => _history.Asked.Count == 1);

        _history.Throw = false;
        _time.Advance(TimeSpan.FromSeconds(60));
        await WaitUntilAsync(() => _history.Asked.Count == 2);
        Assert.Equal((1L, (long?)null), _history.Asked.Last());
        await actor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
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
