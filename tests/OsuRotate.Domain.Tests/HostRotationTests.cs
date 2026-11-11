using OsuRotate.Domain;

namespace OsuRotate.Domain.Tests;

// Ported from osu-ahr's AutoHostSelectorTest. Test names follow the originals so they can be compared.
public class HostRotationTests
{
    private readonly LobbyHarness _lobby = new();

    private void AssertQueue(params string[] expected) => Assert.Equal(expected, _lobby.Queue);

    public class StateTransitions
    {
        private readonly LobbyHarness _lobby = new();

        [Fact]
        public void Starts_empty() => _lobby.AssertState("s0");

        [Fact]
        public void s0_to_hr()
        {
            _lobby.AddPlayers("player1");
            _lobby.AssertState("hr");
        }

        [Fact]
        public void s0_to_s1_to_hr()
        {
            _lobby.Holding = true;
            _lobby.AddPlayers("player1");
            _lobby.AssertState("s1");

            _lobby.Release();
            _lobby.AssertState("hr");
        }

        [Fact]
        public void s0_to_hr_to_s0()
        {
            _lobby.AddPlayers("player1");
            _lobby.AssertState("hr");
            _lobby.RemovePlayer("player1");
            _lobby.AssertState("s0");
        }

        [Fact]
        public void hr1_to_hr3_to_s0()
        {
            _lobby.AddPlayers("player1", "player2", "player3");
            _lobby.AssertHost("player1");
            _lobby.RemovePlayer("player2");
            _lobby.AssertState("hr");
            _lobby.AssertHost("player1");
            _lobby.RemovePlayer("player1");
            _lobby.AssertState("hr");
            _lobby.AssertHost("player3");
            _lobby.RemovePlayer("player3");
            _lobby.AssertState("s0");
        }

        [Fact]
        public void hr_to_m_to_hr()
        {
            _lobby.AddPlayers("player1", "player2", "player3");
            _lobby.PlayMatch();
            _lobby.AssertState("hr");
            _lobby.AssertHost("player2");
        }

        [Fact]
        public void hr_to_m_to_hr_repeated()
        {
            _lobby.AddPlayers("player1", "player2", "player3");
            foreach (var next in new[] { "player2", "player3", "player1" })
            {
                _lobby.PlayMatch();
                _lobby.AssertState("hr");
                _lobby.AssertHost(next);
            }
        }

        [Fact]
        public void hr_to_hn_to_m_to_hr()
        {
            _lobby.AddPlayers("player1", "player2", "player3");
            _lobby.ChangeMap();
            _lobby.AssertState("hr");
            _lobby.RemovePlayer("player1");
            _lobby.AssertState("hn");
            _lobby.AssertHost("player2");
            _lobby.PlayMatch();
            _lobby.AssertState("hr");
            _lobby.AssertHost("player2");
        }

        [Fact]
        public void hr_leave_hn_change_map_hr_match_hr()
        {
            _lobby.AddPlayers("player1", "player2", "player3");
            _lobby.ChangeMap();
            _lobby.RemovePlayer("player1");
            _lobby.AssertState("hn");
            _lobby.AssertHost("player2");
            _lobby.ChangeMap();
            _lobby.AssertState("hr");
            _lobby.PlayMatch();
            _lobby.AssertState("hr");
            _lobby.AssertHost("player3");
        }

        [Fact]
        public void hr_transfer_hn_change_map_hr_match_hr()
        {
            _lobby.AddPlayers("player1", "player2", "player3");
            _lobby.ChangeMap();
            _lobby.ChangeHostManually("player2");
            _lobby.AssertState("hn");
            _lobby.AssertHost("player2");
            _lobby.ChangeMap();
            _lobby.AssertState("hr");
            _lobby.AssertHost("player2");
            _lobby.PlayMatch();
            _lobby.AssertState("hr");
            _lobby.AssertHost("player3");
        }

        [Fact]
        public void hr_m_abort_hn()
        {
            _lobby.AddPlayers("player1", "player2", "player3");
            _lobby.ChangeMap();
            _lobby.PlayMatchAndAbort();
            _lobby.AssertState("hn");
            _lobby.AssertHost("player1");
        }

        // After an abort, a host who changes the map passes host to the next player.
        [Fact]
        public void hr_m_abort_hn_map_change_hn_hr()
        {
            _lobby.AddPlayers("player1", "player2", "player3");
            _lobby.ChangeMap();
            _lobby.PlayMatchAndAbort();
            _lobby.AssertState("hn");
            _lobby.AssertHost("player1");
            _lobby.ChangeMap();
            _lobby.AssertState("hn");
            _lobby.AssertHost("player2");
            _lobby.ChangeMap();
            _lobby.AssertState("hr");
            _lobby.AssertHost("player2");
        }

        [Fact]
        public void hr_m_abort_hn_leave_hn_hr()
        {
            _lobby.AddPlayers("player1", "player2", "player3");
            _lobby.ChangeMap();
            _lobby.PlayMatchAndAbort();
            _lobby.AssertState("hn");
            _lobby.RemovePlayer("player1");
            _lobby.AssertState("hn");
            _lobby.AssertHost("player2");
            _lobby.ChangeMap();
            _lobby.AssertState("hr");
            _lobby.AssertHost("player2");
        }

        [Fact]
        public void hr_s0_hr()
        {
            _lobby.AddPlayers("player1");
            _lobby.ChangeMap();
            _lobby.RemovePlayer("player1");
            _lobby.AssertState("s0");
            _lobby.AddPlayers("player1");
            _lobby.AssertHost("player1");
            _lobby.AssertState("hr");
        }

        [Fact]
        public void hr_s0_hn_map_change_hr()
        {
            _lobby.AddPlayers("player1");
            _lobby.ChangeMap();
            _lobby.RemovePlayer("player1");
            _lobby.AssertState("s0");
            _lobby.AddPlayers("player2", "player3");
            _lobby.AssertHost("player2");
            _lobby.AssertState("hn");
            _lobby.PlayMatch();
            _lobby.AssertHost("player2");
            _lobby.AssertState("hr");
        }
    }

    [Fact]
    public void Newcomers_during_a_match_queue_after_the_current_host()
    {
        _lobby.AddPlayers("player1", "player2");
        _lobby.StartMatch();
        _lobby.AddPlayers("player3");
        _lobby.FinishMatch();
        _lobby.AssertState("hr");
        _lobby.AssertHost("player2");

        _lobby.PlayMatch();
        _lobby.AssertHost("player1");
    }

    [Fact]
    public void Players_leaving_during_matches()
    {
        _lobby.AddPlayers("player1", "player2", "player3");

        _lobby.StartMatch();
        _lobby.RemovePlayer("player3");
        _lobby.FinishMatch();
        _lobby.AssertState("hr");
        _lobby.AssertHost("player2");

        _lobby.StartMatch();
        _lobby.RemovePlayer("player2");
        _lobby.FinishMatch();
        _lobby.AssertHost("player1");

        _lobby.AddPlayers("player4", "player5", "player6");
        _lobby.StartMatch();
        _lobby.RemovePlayer("player1");
        _lobby.FinishMatch();
        _lobby.AssertState("hr");
        _lobby.AssertHost("player4");
    }

    [Fact]
    public void Manual_host_transfers_are_corrected()
    {
        _lobby.AddPlayers("player1", "player2", "player3");

        _lobby.ChangeHostManually("player2");
        _lobby.AssertHost("player2");
        _lobby.ChangeHostManually("player1");
        _lobby.AssertHost("player3");
        _lobby.ChangeHostManually("player3");
        _lobby.AssertHost("player3");
        _lobby.ChangeHostManually("player2");
        _lobby.AssertHost("player1");
    }

    [Fact]
    public void The_next_player_becomes_host_when_the_host_leaves()
    {
        _lobby.AddPlayers("player1", "player2", "player3");
        _lobby.RemovePlayer("player1");
        _lobby.AssertHost("player2");
    }

    [Theory]
    [InlineData("player3")]
    [InlineData("player2")]
    public void A_manual_transfer_racing_the_bot_still_ends_with_the_right_host(string manualTarget)
    {
        _lobby.AddPlayers("player1", "player2", "player3");
        _lobby.StartMatch();
        _lobby.Holding = true;
        _lobby.FinishMatch();

        _lobby.ChangeHostManually(manualTarget);
        _lobby.Release();

        _lobby.AssertHost("player2");
    }

    // osu-ahr issue #37: the host leaves and the match starts before Bancho confirms the new host.
    [Fact]
    public void Issue_37_host_left_and_match_started_at_the_same_time()
    {
        _lobby.AddPlayers("player1", "player2", "player3");
        _lobby.ChangeMap();
        _lobby.Holding = true;
        _lobby.RemovePlayer("player1");
        _lobby.StartMatch();
        _lobby.Release();
        _lobby.AssertHost("player2");
        _lobby.AssertState("m");

        _lobby.FinishMatch();
        _lobby.AssertHost("player2");
        _lobby.AssertState("hr");
    }

    [Fact]
    public void Issue_37_rotation_continues_afterwards()
    {
        _lobby.AddPlayers("player1", "player2", "player3");
        _lobby.ChangeMap();
        _lobby.Holding = true;
        _lobby.RemovePlayer("player1");
        _lobby.StartMatch();
        _lobby.Release();
        _lobby.FinishMatch();
        _lobby.AssertHost("player2");

        _lobby.ChangeMap();
        _lobby.PlayMatch();
        _lobby.AssertHost("player3");
    }

    [Fact]
    public void Issue_37_when_player2_does_not_change_the_map()
    {
        _lobby.AddPlayers("player1", "player2", "player3");
        _lobby.ChangeMap();
        _lobby.Holding = true;
        _lobby.RemovePlayer("player1");
        _lobby.StartMatch();
        _lobby.Release();
        _lobby.FinishMatch();
        _lobby.AssertHost("player2");

        _lobby.PlayMatch();
        _lobby.AssertHost("player3");
    }

    [Fact]
    public void Skip_passes_host_to_the_next_player()
    {
        _lobby.AddPlayers("player1", "player2", "player3");
        _lobby.Run(_lobby.Engine.Skip);
        _lobby.AssertHost("player2");
    }

    [Fact]
    public void Skip_to_a_player_keeps_the_order()
    {
        _lobby.AddPlayers("player1", "player2", "player3");

        _lobby.Run(() => _lobby.Engine.SkipTo("player3"));
        _lobby.AssertHost("player3");
        AssertQueue("player3", "player1", "player2");

        _lobby.Run(() => _lobby.Engine.SkipTo("player3"));
        _lobby.AssertHost("player3");
        AssertQueue("player3", "player1", "player2");
    }

    [Fact]
    public void Map_change_then_host_leaves_then_new_host_changes_map_rotates_normally()
    {
        _lobby.AddPlayers("player1", "player2", "player3");
        _lobby.PlayMatch();
        _lobby.AssertHost("player2");
        _lobby.ChangeMap();
        _lobby.RemovePlayer("player2");
        _lobby.AssertState("hn");
        _lobby.AssertHost("player3");
        _lobby.ChangeMap();
        _lobby.AssertState("hr");
        _lobby.PlayMatch();
        _lobby.AssertHost("player1");
    }

    [Fact]
    public void Map_change_then_host_leaves_then_match_keeps_the_new_host_once()
    {
        _lobby.AddPlayers("player1", "player2", "player3");
        _lobby.PlayMatch();
        _lobby.ChangeMap();
        _lobby.AssertHost("player2");
        _lobby.ChangeMap();
        _lobby.RemovePlayer("player2");
        _lobby.AssertState("hn");
        _lobby.AssertHost("player3");
        _lobby.PlayMatch();
        _lobby.AssertState("hr");
        _lobby.AssertHost("player3");
        _lobby.PlayMatch();
        _lobby.AssertHost("player1");
    }

    public class Aborts
    {
        private readonly LobbyHarness _lobby = new();

        [Fact]
        public void Abort_before_anyone_finished_keeps_the_host()
        {
            _lobby.AddPlayers("player1", "player2", "player3");
            _lobby.PlayMatchAndAbort();
            _lobby.AssertState("hn");
            _lobby.AssertHost("player1");

            _lobby.PlayMatch();
            _lobby.AssertState("hr");
            _lobby.AssertHost("player2");
        }

        [Fact]
        public void Abort_after_someone_finished_counts_as_played()
        {
            _lobby.AddPlayers("player1", "player2", "player3");
            _lobby.PlayMatchAndAbort("player1");
            _lobby.AssertState("hr");
            _lobby.AssertHost("player2");

            _lobby.PlayMatch();
            _lobby.AssertHost("player3");
        }

        [Fact]
        public void Abort_then_map_change_passes_host()
        {
            _lobby.AddPlayers("player1", "player2", "player3");
            _lobby.PlayMatchAndAbort();
            _lobby.AssertState("hn");
            _lobby.AssertHost("player1");
            _lobby.ChangeMap();
            _lobby.AssertState("hn");
            _lobby.AssertHost("player2");
            _lobby.PlayMatch();
            _lobby.AssertState("hr");
            _lobby.AssertHost("player2");
        }

        [Fact]
        public void Host_leaves_mid_match_then_abort_gives_the_next_player_a_full_turn()
        {
            var players = _lobby.AddPlayers(5);
            _lobby.PlayMatch();
            _lobby.ChangeMap();
            _lobby.AssertHost(players[1]);
            _lobby.StartMatch();
            _lobby.RemovePlayer(players[1]);
            _lobby.AssertState("m");
            Assert.Null(_lobby.Engine.Room.Host);

            _lobby.AbortMatch();
            _lobby.AssertState("hn");
            _lobby.AssertHost(players[2]);
            _lobby.PlayMatch();
            _lobby.AssertState("hr");
            _lobby.AssertHost(players[2]);
        }

        [Fact]
        public void Host_leaves_mid_match_then_abort_then_map_change_keeps_the_new_host()
        {
            var players = _lobby.AddPlayers(5);
            _lobby.PlayMatch();
            _lobby.ChangeMap();
            _lobby.StartMatch();
            _lobby.RemovePlayer(players[1]);
            _lobby.AbortMatch();
            _lobby.AssertState("hn");
            _lobby.AssertHost(players[2]);
            _lobby.ChangeMap();
            _lobby.AssertState("hr");
            _lobby.AssertHost(players[2]);
        }

        [Fact]
        public void Host_leaves_then_others_finish_then_abort_moves_on()
        {
            _lobby.AddPlayers("a", "b", "c", "d");
            _lobby.PlayMatch();
            _lobby.ChangeMap();
            _lobby.AssertHost("b");
            _lobby.StartMatch();
            _lobby.RemovePlayer("b");
            Assert.Null(_lobby.Engine.Room.Host);
            _lobby.AbortMatch("a", "c", "d");
            _lobby.AssertState("hr");
            _lobby.AssertHost("c");
            _lobby.ChangeMap();
            _lobby.AssertState("hr");
            _lobby.AssertHost("c");
        }

        // osu-ahr treated this reply as an abort even with no match running, which wrongly
        // cancelled the next rotation.
        [Fact]
        public void A_not_in_progress_reply_outside_a_match_changes_nothing()
        {
            _lobby.AddPlayers("player1", "player2");
            _lobby.Feed(new MatchNotInProgress());
            _lobby.AssertState("hr");
        }
    }

    public class RoomStates_
    {
        private readonly LobbyHarness _lobby = new();

        private void AssertQueue(params string[] expected) => Assert.Equal(expected, _lobby.Queue);

        [Fact]
        public void Empty_lobby_takes_slot_order()
        {
            _lobby.ApplyRoomState(RoomStates.Case1_1);
            AssertQueue("p1", "p2", "p3", "p4", "p5");
        }

        [Fact]
        public void Empty_lobby_starts_from_the_current_host()
        {
            _lobby.ApplyRoomState(RoomStates.Case1_2);
            AssertQueue("p3", "p4", "p5", "p1", "p2");
        }

        [Fact]
        public void A_host_change_seen_in_settings_moves_the_queue()
        {
            _lobby.ApplyRoomState(RoomStates.Case1_1);
            _lobby.Run(() => _lobby.Engine.SkipTo("p3"));
            AssertQueue("p3", "p4", "p5", "p1", "p2");
            _lobby.AssertHost("p3");

            _lobby.ApplyRoomState(RoomStates.Case1_1);
            AssertQueue("p1", "p2", "p3", "p4", "p5");
            _lobby.AssertHost("p1");
        }

        [Fact]
        public void Players_who_came_and_went_are_merged_into_the_queue()
        {
            _lobby.ApplyRoomState(RoomStates.Case1_1);
            _lobby.ApplyRoomState(RoomStates.Case1_3);
            AssertQueue("p4", "p5", "p6", "p7", "p2");
            _lobby.AssertHost("p4");
        }

        [Fact]
        public void A_host_missing_from_the_queue_resets_to_slot_order()
        {
            _lobby.ApplyRoomState(RoomStates.Case1_1);
            _lobby.RemovePlayer("p1");
            _lobby.Run(() => _lobby.Engine.SkipTo("p3"));
            _lobby.ApplyRoomState(RoomStates.Case1_1);
            AssertQueue("p1", "p2", "p3", "p4", "p5");
            _lobby.AssertHost("p1");
        }
    }

    public class Reordering
    {
        private readonly LobbyHarness _lobby = new();

        private void AssertQueue(params string[] expected) => Assert.Equal(expected, _lobby.Queue);

        [Fact]
        public void Reorders_by_name()
        {
            _lobby.AddPlayers(5);
            _lobby.Run(() => _lobby.Engine.Reorder("p3,p1,p2,p4,p0"));
            _lobby.AssertHost("p3");
            AssertQueue("p3", "p1", "p2", "p4", "p0");
        }

        [Fact]
        public void Accepts_names_copied_from_the_queue_message()
        {
            _lobby.AddPlayers(5);
            _lobby.Run(() => _lobby.Engine.SkipTo("p3"));
            _lobby.Run(() => _lobby.Engine.Reorder("p​0, p​1, p​2, p​3, p​4"));
            _lobby.AssertHost("p0");
            AssertQueue("p0", "p1", "p2", "p3", "p4");
        }

        [Fact]
        public void An_unchanged_order_changes_nothing()
        {
            _lobby.AddPlayers(5);
            _lobby.Run(() => _lobby.Engine.Reorder("p​0, p​1, p​2, p​3, p​4"));
            _lobby.AssertHost("p0");
            AssertQueue("p0", "p1", "p2", "p3", "p4");
        }

        [Fact]
        public void Unlisted_players_follow_in_their_current_order()
        {
            _lobby.AddPlayers(5);
            _lobby.Run(() => _lobby.Engine.Reorder("p​3, p​4, p2"));
            _lobby.AssertHost("p3");
            AssertQueue("p3", "p4", "p2", "p0", "p1");
        }

        [Fact]
        public void Names_not_in_the_room_are_ignored()
        {
            _lobby.AddPlayers(5);
            _lobby.Run(() => _lobby.Engine.Reorder("p3, p6, p4, p2, p5, p0, p1"));
            _lobby.AssertHost("p3");
            AssertQueue("p3", "p4", "p2", "p0", "p1");
        }

        [Fact]
        public void The_owner_can_reorder_from_chat()
        {
            _lobby.AddPlayers(5);
            _lobby.Run(() => _lobby.Engine.SkipTo("p3"));
            _lobby.AssertHost("p3");

            _lobby.Chat(LobbyHarness.Owner, "*reorder p​0, p​1, p​2, p​3, p​4");

            AssertQueue("p0", "p1", "p2", "p3", "p4");
        }

        [Theory]
        [InlineData("*reorder")]
        [InlineData("*reorder asdfsafasdf")]
        public void Invalid_reorder_commands_change_nothing(string command)
        {
            _lobby.AddPlayers(5);
            _lobby.Run(() => _lobby.Engine.SkipTo("p3"));

            _lobby.Chat(LobbyHarness.Owner, command);

            AssertQueue("p3", "p4", "p0", "p1", "p2");
        }

        [Fact]
        public void Players_cannot_use_owner_commands()
        {
            _lobby.AddPlayers(5);

            _lobby.Chat("p2", "*reorder p2, p1");
            _lobby.Chat("p2", "*skip");

            _lobby.AssertHost("p0");
            AssertQueue("p0", "p1", "p2", "p3", "p4");
        }
    }

    public class ClearedHost
    {
        private readonly LobbyHarness _lobby = new();

        [Fact]
        public void A_match_after_clearhost_rotates()
        {
            _lobby.AddPlayers("player1", "player2", "player3");
            _lobby.Feed(new HostCleared());
            Assert.True(_lobby.Engine.Room.IsHostCleared);
            Assert.Null(_lobby.Engine.Room.Host);

            _lobby.PlayMatch();
            Assert.Equal("player2", _lobby.Engine.Room.Host?.Name);
        }

        [Fact]
        public void Skip_after_clearhost_appoints_the_next_player()
        {
            _lobby.AddPlayers("player1", "player2", "player3");
            _lobby.Feed(new HostCleared());

            _lobby.Run(_lobby.Engine.Skip);

            Assert.Equal("player2", _lobby.Engine.Room.Host?.Name);
        }
    }

    public class DenyLists
    {
        private readonly LobbyHarness _lobby = new();

        private void AssertQueue(params string[] expected) => Assert.Equal(expected, _lobby.Queue);

        private void Deny(string name) => _lobby.Run(() => _lobby.Engine.Deny(name));

        private void Allow(string name) => _lobby.Run(() => _lobby.Engine.Allow(name));

        [Fact]
        public void Denied_players_are_left_out()
        {
            Deny("p3");
            _lobby.ApplyRoomState(RoomStates.Case1_1);
            AssertQueue("p1", "p2", "p4", "p5");
        }

        [Fact]
        public void A_denied_player_joining_later_is_never_queued()
        {
            _lobby.AddPlayers("p1");
            Deny("late");

            _lobby.AddPlayers("late");
            _lobby.PlayMatch();

            AssertQueue("p1");
            _lobby.AssertHost("p1");
        }

        [Fact]
        public void Host_given_to_a_denied_player_is_taken_back()
        {
            Deny("p4");
            _lobby.ApplyRoomState(RoomStates.Case1_1);
            AssertQueue("p1", "p2", "p3", "p5");
            _lobby.AssertHost("p1");

            _lobby.ChangeHostManually("p4");
            _lobby.AssertHost("p2");
            _lobby.ChangeHostManually("p4");
            _lobby.AssertHost("p3");
            _lobby.ChangeHostManually("p4");
            _lobby.AssertHost("p5");
            _lobby.ChangeHostManually("p1");
            _lobby.AssertHost("p1");
        }

        [Fact]
        public void A_room_of_only_denied_players_has_no_host()
        {
            Deny("p4");
            Deny("p5");
            _lobby.ApplyRoomState(RoomStates.Case1_1);
            Assert.Equal(3, _lobby.Queue.Count);

            _lobby.AssertHost("p1");
            _lobby.RemovePlayer("p1");
            _lobby.AssertHost("p2");
            _lobby.RemovePlayer("p3");
            _lobby.AssertHost("p2");
            _lobby.RemovePlayer("p2");
            Assert.Empty(_lobby.Queue);
            _lobby.AssertNoHost();
            _lobby.RemovePlayer("p4");
            _lobby.AssertNoHost();
        }

        [Fact]
        public void A_newcomer_to_a_room_of_denied_players_becomes_host()
        {
            Deny("p4");
            Deny("p5");
            _lobby.ApplyRoomState(RoomStates.Case1_1);
            _lobby.RemovePlayer("p1");
            _lobby.RemovePlayer("p2");
            _lobby.RemovePlayer("p3");
            _lobby.AssertNoHost();

            _lobby.AddPlayers("p6");
            _lobby.AssertHost("p6");
        }

        [Fact]
        public void Cannot_skip_to_a_denied_player()
        {
            Deny("p4");
            _lobby.ApplyRoomState(RoomStates.Case1_1);
            _lobby.Run(() => _lobby.Engine.SkipTo("p3"));
            _lobby.AssertHost("p3");
            _lobby.Run(() => _lobby.Engine.SkipTo("p4"));
            _lobby.AssertHost("p3");
        }

        [Fact]
        public void Denying_a_player_removes_them_from_the_queue()
        {
            _lobby.ApplyRoomState(RoomStates.Case1_1);
            Deny("p3");
            AssertQueue("p1", "p2", "p4", "p5");
            _lobby.AssertHost("p1");
        }

        [Fact]
        public void Denying_the_host_passes_host_on()
        {
            _lobby.ApplyRoomState(RoomStates.Case1_1);
            Deny("p1");
            _lobby.AssertHost("p2");
            AssertQueue("p2", "p3", "p4", "p5");
        }

        [Fact]
        public void Denying_the_last_eligible_player_clears_the_host()
        {
            _lobby.ApplyRoomState(RoomStates.Case1_1);
            _lobby.RemovePlayer("p1");
            _lobby.RemovePlayer("p2");
            _lobby.RemovePlayer("p3");
            _lobby.AssertHost("p4");
            Deny("p5");
            _lobby.AssertHost("p4");
            Deny("p4");
            Assert.Empty(_lobby.Queue);
            _lobby.AssertNoHost();
        }

        [Fact]
        public void Allowing_a_player_queues_them_at_the_back()
        {
            _lobby.ApplyRoomState(RoomStates.Case1_1);
            Deny("p3");
            _lobby.AssertHost("p1");
            Allow("p3");
            AssertQueue("p1", "p2", "p4", "p5", "p3");
        }

        [Fact]
        public void Allowing_a_player_into_an_empty_queue_makes_them_host()
        {
            _lobby.ApplyRoomState(RoomStates.Case1_1);
            foreach (var name in new[] { "p1", "p2", "p3", "p4", "p5" })
            {
                Deny(name);
            }

            Assert.Empty(_lobby.Queue);
            Allow("p3");
            AssertQueue("p3");
            _lobby.AssertHost("p3");
        }

        [Fact]
        public void Slot_order_skips_denied_players()
        {
            Deny("p4");
            _lobby.ApplyRoomState(RoomStates.Case1_2);
            AssertQueue("p3", "p5", "p1", "p2");
        }

        [Fact]
        public void A_denied_host_in_settings_passes_host_on()
        {
            Deny("p3");
            _lobby.ApplyRoomState(RoomStates.Case1_2);
            AssertQueue("p1", "p2", "p4", "p5");
        }

        [Theory]
        [InlineData("p5", new[] { "p4", "p6", "p7", "p2" })]
        [InlineData("p1", new[] { "p4", "p5", "p6", "p7", "p2" })]
        [InlineData("p7", new[] { "p4", "p5", "p6", "p2" })]
        public void Merging_settings_respects_the_deny_list(string denied, string[] expected)
        {
            Deny(denied);
            _lobby.ApplyRoomState(RoomStates.Case1_1);
            _lobby.ApplyRoomState(RoomStates.Case1_3);
            AssertQueue(expected);
        }

        [Fact]
        public void The_owner_manages_the_deny_list_from_chat()
        {
            _lobby.ApplyRoomState(RoomStates.Case1_1);

            _lobby.Chat(LobbyHarness.Owner, "*denylist add p1");
            _lobby.Chat(LobbyHarness.Owner, "*denylist add p2 sfd");
            _lobby.Chat(LobbyHarness.Owner, "*denylist add");
            _lobby.Chat(LobbyHarness.Owner, "*denylist     add    asdf    hello");
            _lobby.Chat(LobbyHarness.Owner, "*denylist add p1");

            Assert.Equal(["p1", "p2_sfd", "asdf____hello"], _lobby.Engine.DenyList.Keys);

            _lobby.Chat(LobbyHarness.Owner, "*denylist remove p1");
            _lobby.Chat(LobbyHarness.Owner, "*denylist remove p1");
            _lobby.Chat(LobbyHarness.Owner, "*denylist    remove     P2 SFD");

            Assert.Equal(["asdf____hello"], _lobby.Engine.DenyList.Keys);
        }
    }

    public class QueueMessages
    {
        private readonly LobbyHarness _lobby = new();

        [Fact]
        public void Anyone_in_the_room_can_ask_for_the_queue_without_pinging_players()
        {
            _lobby.AddPlayers("alice", "bob");

            _lobby.Chat("bob", "!queue");

            Assert.Equal("Host order: a​lice, b​ob", Assert.Single(_lobby.Said));
        }

        [Fact]
        public void The_queue_message_has_a_cooldown()
        {
            _lobby.AddPlayers("alice", "bob");

            _lobby.Chat("bob", "!q");
            _lobby.Chat("bob", "!q");
            _lobby.Time.Advance(TimeSpan.FromSeconds(16));
            _lobby.Chat("bob", "!q");

            Assert.Equal(2, _lobby.Said.Count());
        }

        [Fact]
        public void The_queue_is_announced_after_each_match()
        {
            _lobby.AddPlayers("alice", "bob");

            _lobby.PlayMatch();

            Assert.Equal("Host order: b​ob, a​lice", Assert.Single(_lobby.Said));
        }

        [Fact]
        public void A_long_queue_is_shortened()
        {
            var names = _lobby.AddPlayers([.. Enumerable.Range(0, 10).Select(i => $"long_player_{i:00}")]);

            _lobby.Chat(names[1], "!queue");

            var text = Assert.Single(_lobby.Said);
            Assert.EndsWith("...", text, StringComparison.Ordinal);
            Assert.Equal("Host order: ".Length + 90 + 3, text.Length);
        }

        [Fact]
        public void People_outside_the_room_get_no_answer()
        {
            _lobby.AddPlayers("alice");

            _lobby.Chat("stranger", "!queue");

            Assert.Empty(_lobby.Said);
        }
    }
}
