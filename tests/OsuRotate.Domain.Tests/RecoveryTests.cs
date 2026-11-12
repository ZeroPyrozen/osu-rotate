using OsuRotate.Domain;

namespace OsuRotate.Domain.Tests;

// Restoring the queue after a restart (phase 4, issue 06). The engine is new; the room is as Bancho
// reports it in !mp settings.
public class RecoveryTests
{
    private static readonly DateTimeOffset MatchStart = new(2025, 12, 31, 23, 58, 0, TimeSpan.Zero);

    private static LobbyHarness Fresh() =>
        new(s => s with { HostAfkCheckInterval = TimeSpan.Zero, Regulation = s.Regulation with { Enabled = false } });

    private static RoomState Room(string? host, params string[] players) =>
        new("room", 7, 4242, "map", "HeadToHead", "Score", string.Empty,
            [.. players.Select((name, i) => new RoomPlayer(i + 1, "Not Ready", (i + 1) * 111, name, name == host, Team.None, name == host ? "Host" : string.Empty))]);

    private static IEnumerable<string> Queue(LobbyHarness lobby) => lobby.Engine.Rotation.Queue.Select(p => p.Name);

    [Fact]
    public void Without_a_plan_the_queue_is_rebuilt_in_slot_order()
    {
        var lobby = Fresh();
        lobby.ApplyRoomState(Room("p1", "p1", "p2", "p3"));
        Assert.Equal(["p1", "p2", "p3"], Queue(lobby));
    }

    [Fact]
    public void Between_matches_the_saved_queue_comes_back_and_its_front_gets_host()
    {
        var lobby = Fresh();
        lobby.Engine.RestoreOnNextRoomState(["p3", "p1", "p2"], "p3", runningMatch: null);
        Assert.True(lobby.Engine.IsRestorePending);
        lobby.ApplyRoomState(Room(null, "p1", "p2", "p3"));

        Assert.False(lobby.Engine.IsRestorePending);
        Assert.Equal(["p3", "p1", "p2"], Queue(lobby));
        Assert.Contains(new TransferHost("p3", 333), lobby.Sent);
        lobby.AssertHost("p3");
    }

    [Fact]
    public void Between_matches_a_host_already_at_the_front_is_left_alone()
    {
        var lobby = Fresh();
        lobby.Engine.RestoreOnNextRoomState(["p2", "p3", "p1"], "p2", runningMatch: null);
        lobby.ApplyRoomState(Room("p2", "p1", "p2", "p3"));

        Assert.Equal(["p2", "p3", "p1"], Queue(lobby));
        Assert.DoesNotContain(lobby.Sent, c => c is TransferHost);
    }

    [Fact]
    public void A_match_that_finished_while_the_bot_was_away_passes_host_to_the_next_player()
    {
        // Saved mid-match: the queue had rotated, p1 still hosted.
        var lobby = Fresh();
        lobby.Engine.RestoreOnNextRoomState(["p2", "p3", "p1"], "p1", runningMatch: null);
        lobby.ApplyRoomState(Room("p1", "p1", "p2", "p3"));

        Assert.Equal(["p2", "p3", "p1"], Queue(lobby));
        lobby.AssertHost("p2");
    }

    [Fact]
    public void A_running_match_keeps_the_host_until_it_finishes_then_the_next_player_hosts()
    {
        var lobby = Fresh();
        lobby.Engine.RestoreOnNextRoomState(["p2", "p3", "p1"], "p1", new RestoredMatch(MatchStart, 4242));
        lobby.ApplyRoomState(Room("p1", "p1", "p2", "p3"));

        Assert.True(lobby.Engine.Room.IsMatching);
        Assert.All(lobby.Engine.Room.Players, p => Assert.Equal(MatchStatus.Playing, p.Status));
        Assert.Equal(["p2", "p3", "p1"], Queue(lobby));
        Assert.DoesNotContain(lobby.Sent, c => c is TransferHost);

        lobby.FinishMatchWith(("p1", 100), ("p2", 200));
        lobby.AssertHost("p2");
        Assert.Equal(["p2", "p3", "p1"], Queue(lobby));
    }

    [Fact]
    public void The_resumed_match_is_recorded_with_its_real_start_map_and_host()
    {
        var lobby = Fresh();
        lobby.Engine.RestoreOnNextRoomState(["p2", "p3", "p1"], "p1", new RestoredMatch(MatchStart, 999));
        lobby.ApplyRoomState(Room("p1", "p1", "p2", "p3"));
        lobby.FinishMatchWith(("p2", 200));

        var match = Assert.Single(lobby.Sent.OfType<RecordMatch>()).Match;
        Assert.Equal(MatchStart, match.StartedAt);
        Assert.Equal(999, match.BeatmapId);
        Assert.Equal("p1", match.HostName);
        Assert.Equal([new MatchRecordScore("p2", 222, 200, true)], match.Scores);
    }

    [Fact]
    public void A_resumed_match_whose_host_left_records_the_saved_host()
    {
        // Bancho reports no host, but the saved host is still in the room.
        var lobby = Fresh();
        lobby.Engine.RestoreOnNextRoomState(["p2", "p3", "p1"], "p1", new RestoredMatch(MatchStart, 999));
        lobby.ApplyRoomState(Room(null, "p1", "p2", "p3"));
        Assert.Null(lobby.Engine.Room.Host);
        lobby.FinishMatchWith(("p2", 200));

        Assert.Equal("p1", lobby.Sent.OfType<RecordMatch>().Single().Match.HostName);
    }

    [Fact]
    public void A_resumed_match_can_be_aborted_by_vote()
    {
        var lobby = Fresh();
        lobby.Engine.RestoreOnNextRoomState(["p2", "p3", "p1"], "p1", new RestoredMatch(MatchStart, 999));
        lobby.ApplyRoomState(Room("p1", "p1", "p2", "p3"));
        lobby.Chat("p2", "!abort");
        lobby.Chat("p3", "!abort");

        Assert.Contains(lobby.Sent, c => c is AbortMatch);
    }

    [Fact]
    public void A_match_that_ends_before_the_room_state_arrives_is_not_resumed()
    {
        var lobby = Fresh();
        lobby.Engine.RestoreOnNextRoomState(["p2", "p3", "p1"], "p1", new RestoredMatch(MatchStart, 999));
        lobby.Feed(new MatchFinished());
        lobby.ApplyRoomState(Room("p1", "p1", "p2", "p3"));

        Assert.False(lobby.Engine.Room.IsMatching);
        lobby.AssertHost("p2");
    }

    [Fact]
    public void An_abort_before_the_room_state_also_means_no_match_to_resume()
    {
        var lobby = Fresh();
        lobby.ApplyRoomState(Room(null));
        lobby.Feed(new MatchStarted());
        lobby.Engine.RestoreOnNextRoomState(["p2", "p1"], "p1", new RestoredMatch(MatchStart, 999));
        lobby.Feed(new MatchAborted());
        lobby.ApplyRoomState(Room("p1", "p1", "p2"));

        Assert.False(lobby.Engine.Room.IsMatching);
    }

    [Fact]
    public void Players_who_left_are_dropped_and_newcomers_follow_in_slot_order()
    {
        var lobby = Fresh();
        lobby.Engine.RestoreOnNextRoomState(["p3", "gone", "p1"], "p3", runningMatch: null);
        lobby.ApplyRoomState(Room(null, "new2", "p1", "new1", "p3"));

        Assert.Equal(["p3", "p1", "new2", "new1"], Queue(lobby));
        lobby.AssertHost("p3");
    }

    [Fact]
    public void A_player_who_joined_and_left_before_the_room_state_is_dropped()
    {
        var lobby = Fresh();
        lobby.Engine.RestoreOnNextRoomState(["p3", "p1", "p2"], "p3", runningMatch: null);
        lobby.Feed(new PlayerJoined("p3", 3, Team.None));
        lobby.Feed(new PlayerLeft("p3"));
        lobby.ApplyRoomState(Room(null, "p1", "p2"));

        Assert.Equal(["p1", "p2"], Queue(lobby));
        lobby.AssertHost("p1");
    }

    [Fact]
    public void If_the_saved_front_left_the_next_saved_player_hosts()
    {
        var lobby = Fresh();
        lobby.Engine.RestoreOnNextRoomState(["gone", "p2", "p1"], "gone", runningMatch: null);
        lobby.ApplyRoomState(Room(null, "p1", "p2"));

        Assert.Equal(["p2", "p1"], Queue(lobby));
        lobby.AssertHost("p2");
    }

    [Fact]
    public void Denied_players_stay_out_of_the_restored_queue()
    {
        var lobby = Fresh();
        lobby.Engine.Deny("p2");
        lobby.Engine.RestoreOnNextRoomState(["p2", "p3", "p1"], "p2", runningMatch: null);
        lobby.ApplyRoomState(Room(null, "p1", "p2", "p3"));

        Assert.Equal(["p3", "p1"], Queue(lobby));
        lobby.AssertHost("p3");
    }

    [Fact]
    public void An_empty_room_restores_an_empty_queue()
    {
        var lobby = Fresh();
        lobby.Engine.RestoreOnNextRoomState(["p1", "p2"], "p1", runningMatch: null);
        lobby.ApplyRoomState(Room(null));

        Assert.Empty(Queue(lobby));
        Assert.DoesNotContain(lobby.Sent, c => c is TransferHost);
    }

    [Fact]
    public void Only_the_first_room_state_is_used_for_the_restore()
    {
        var lobby = Fresh();
        lobby.Engine.RestoreOnNextRoomState(["p3", "p1", "p2"], "p3", runningMatch: null);
        lobby.ApplyRoomState(Room(null, "p1", "p2", "p3"));
        lobby.ApplyRoomState(Room("p3", "p1", "p2", "p3", "p4"));

        Assert.Equal(["p3", "p1", "p2", "p4"], Queue(lobby));
    }

    [Fact]
    public void After_the_restore_rotation_carries_on_as_usual()
    {
        var lobby = Fresh();
        lobby.Engine.RestoreOnNextRoomState(["p3", "p1", "p2"], "p3", runningMatch: null);
        lobby.ApplyRoomState(Room(null, "p1", "p2", "p3"));
        lobby.PickMap(100);
        lobby.Feed(new MatchStarted());
        lobby.FinishMatchWith(("p3", 1));

        lobby.AssertHost("p1");
        Assert.Equal(["p1", "p2", "p3"], Queue(lobby));
    }
}
