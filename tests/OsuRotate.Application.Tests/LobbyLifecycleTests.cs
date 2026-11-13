using OsuRotate.Application;
using OsuRotate.Domain;

namespace OsuRotate.Application.Tests;

public class LobbyLifecycleTests
{
    private static readonly LobbySettings Settings = new() { OwnerName = "creator" };
    private static readonly RoomState EmptyRoom = new("my room", 7, 0, string.Empty, "HeadToHead", "Score", string.Empty, []);

    private static LifecycleStep On(LobbyLifecycle lifecycle, LobbyEvent e) => lifecycle.OnEvent(e, Settings);

    private static LifecycleStep Do(LobbyLifecycle lifecycle, LobbyInput input, LobbySettings? settings = null) =>
        lifecycle.OnInput(input, settings ?? Settings);

    private static LobbyLifecycle Connected()
    {
        var lifecycle = new LobbyLifecycle();
        On(lifecycle, new TransportConnected());
        return lifecycle;
    }

    private static LobbyLifecycle OpenRoom(long roomId = 7)
    {
        var lifecycle = Connected();
        Do(lifecycle, new CreateLobby("my room"));
        On(lifecycle, new RoomJoined(roomId));
        On(lifecycle, new RoomCreated(roomId, "my room"));
        return lifecycle;
    }

    public class Creating
    {
        [Fact]
        public void Starts_idle()
        {
            var lifecycle = new LobbyLifecycle();
            Assert.Equal(LobbyStatus.Idle, lifecycle.Status);
            Assert.Null(lifecycle.RoomId);
            Assert.False(On(lifecycle, new TransportConnected()).Commands.Any());
            Assert.Equal(LobbyStatus.Idle, lifecycle.Status);
        }

        [Fact]
        public void Tracks_whether_bancho_is_connected()
        {
            var lifecycle = new LobbyLifecycle();
            Assert.False(lifecycle.Connected);
            On(lifecycle, new TransportConnected());
            Assert.True(lifecycle.Connected);
            On(lifecycle, new TransportDisconnected("gone"));
            Assert.False(lifecycle.Connected);
            On(lifecycle, new TransportConnected());
            On(lifecycle, new TransportAuthenticationFailed());
            Assert.False(lifecycle.Connected);
        }

        [Fact]
        public void Create_sends_mp_make_and_starts_the_engine_over()
        {
            var lifecycle = Connected();
            var step = Do(lifecycle, new CreateLobby("5* rotate"));
            Assert.Equal([new CreateRoom("5* rotate")], step.Commands);
            Assert.True(step.ResetEngine);
            Assert.Equal(LobbyStatus.Creating, lifecycle.Status);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Only_room_created_reaches_the_engine_whichever_reply_comes_first(bool joinFirst)
        {
            var lifecycle = Connected();
            Do(lifecycle, new CreateLobby("my room"));
            var steps = joinFirst
                ? new[] { On(lifecycle, new RoomJoined(7)), On(lifecycle, new RoomCreated(7, "my room")) }
                : [On(lifecycle, new RoomCreated(7, "my room")), On(lifecycle, new RoomJoined(7))];

            Assert.Equal([!joinFirst, joinFirst], steps.Select(s => s.Forward));
            Assert.Equal(LobbyStatus.Open, lifecycle.Status);
            Assert.Equal(7, lifecycle.RoomId);
            Assert.Equal("my room", lifecycle.Title);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void A_blank_title_gets_the_default(string title)
        {
            var step = Do(Connected(), new CreateLobby(title));
            Assert.Equal([new CreateRoom(LobbyLifecycle.DefaultTitle)], step.Commands);
        }

        [Fact]
        public void Create_before_connecting_waits_for_the_connection()
        {
            var lifecycle = new LobbyLifecycle();
            Assert.Empty(Do(lifecycle, new CreateLobby("my room")).Commands);
            Assert.Equal(LobbyStatus.Connecting, lifecycle.Status);

            var step = On(lifecycle, new TransportConnected());
            Assert.Equal([new CreateRoom("my room")], step.Commands);
            Assert.Equal(LobbyStatus.Creating, lifecycle.Status);
        }

        [Fact]
        public void A_second_create_is_ignored()
        {
            var lifecycle = Connected();
            Do(lifecycle, new CreateLobby("a"));
            Assert.Empty(Do(lifecycle, new CreateLobby("b")).Commands);

            On(lifecycle, new RoomCreated(7, "a"));
            Assert.Empty(Do(lifecycle, new CreateLobby("b")).Commands);
            Assert.Empty(Do(lifecycle, new AttachLobby(9)).Commands);
            Assert.Equal(7, lifecycle.RoomId);
        }

        [Fact]
        public void A_connection_lost_while_creating_creates_again()
        {
            var lifecycle = Connected();
            Do(lifecycle, new CreateLobby("my room"));
            On(lifecycle, new TransportDisconnected("gone"));
            Assert.Equal(LobbyStatus.Connecting, lifecycle.Status);
            Assert.Equal([new CreateRoom("my room")], On(lifecycle, new TransportConnected()).Commands);
        }

        [Fact]
        public void A_connection_lost_after_the_join_but_before_the_reply_rejoins_that_room()
        {
            var lifecycle = Connected();
            Do(lifecycle, new CreateLobby("my room"));
            On(lifecycle, new RoomJoined(7));
            On(lifecycle, new TransportDisconnected("gone"));
            Assert.Equal(LobbyStatus.Reconnecting, lifecycle.Status);
            Assert.Equal([new JoinRoom(7)], On(lifecycle, new TransportConnected()).Commands);
        }

        [Fact]
        public void Events_with_no_room_do_not_reach_the_engine()
        {
            var lifecycle = Connected();
            Assert.False(On(lifecycle, new PlayerJoined("p1", 1, Team.None)).Forward);
            Assert.False(On(lifecycle, new ChatReceived("p1", "hi", false)).Forward);
            Assert.False(On(lifecycle, new RoomCreated(5, "stray")).Forward);
            Assert.False(On(lifecycle, new RoomJoined(5)).Forward);
        }

        [Fact]
        public void Events_in_an_open_room_reach_the_engine()
        {
            var lifecycle = OpenRoom();
            Assert.True(On(lifecycle, new PlayerJoined("p1", 1, Team.None)).Forward);
            Assert.True(On(lifecycle, new ChatReceived("p1", "hi", false)).Forward);
        }
    }

    public class Attaching
    {
        [Fact]
        public void Attach_joins_and_syncs_before_play()
        {
            var lifecycle = Connected();
            var step = Do(lifecycle, new AttachLobby(42));
            Assert.Equal([new JoinRoom(42)], step.Commands);
            Assert.True(step.ResetEngine);
            Assert.Equal(LobbyStatus.Rejoining, lifecycle.Status);

            step = On(lifecycle, new RoomJoined(42));
            Assert.Equal([new RequestRoomState()], step.Commands);
            Assert.True(step.Forward);
            Assert.Equal(LobbyStatus.Syncing, lifecycle.Status);

            step = On(lifecycle, new RoomStateReceived(EmptyRoom));
            Assert.True(step.Forward);
            Assert.Equal(LobbyStatus.Open, lifecycle.Status);
            Assert.Equal("my room", lifecycle.Title);
        }

        [Fact]
        public void Attach_before_connecting_joins_once_connected()
        {
            var lifecycle = new LobbyLifecycle();
            Assert.Empty(Do(lifecycle, new AttachLobby(42)).Commands);
            Assert.Equal(LobbyStatus.Connecting, lifecycle.Status);
            Assert.Equal([new JoinRoom(42)], On(lifecycle, new TransportConnected()).Commands);
        }

        [Fact]
        public void A_room_the_owner_picked_that_is_gone_is_not_replaced()
        {
            var lifecycle = Connected();
            Do(lifecycle, new AttachLobby(42));
            var step = On(lifecycle, new RoomJoinFailed(42, "No such channel"));
            Assert.Empty(step.Commands);
            Assert.True(step.ResetEngine);
            Assert.Equal(LobbyStatus.Idle, lifecycle.Status);
            Assert.Null(lifecycle.RoomId);
        }

        // Bancho only lets the bot into a room it referees (design UC2); the owner is told why.
        [Fact]
        public void A_refused_attach_says_why()
        {
            var lifecycle = Connected();
            Do(lifecycle, new AttachLobby(42));
            Assert.Null(lifecycle.Notice);

            On(lifecycle, new RoomJoinFailed(42, "No such channel"));
            Assert.Equal(
                "Could not attach to room 42 (Bancho: No such channel). The room must be open, and the bot's account must be its creator or a referee.",
                lifecycle.Notice);
        }

        [Fact]
        public void The_next_room_action_clears_the_notice()
        {
            var lifecycle = Connected();
            Do(lifecycle, new AttachLobby(42));
            On(lifecycle, new RoomJoinFailed(42, "No such channel"));

            Do(lifecycle, new AttachLobby(43));
            Assert.Null(lifecycle.Notice);
        }

        [Fact]
        public void A_successful_attach_has_no_notice()
        {
            var lifecycle = Connected();
            Do(lifecycle, new AttachLobby(7));
            On(lifecycle, new RoomJoined(7));
            On(lifecycle, new RoomStateReceived(EmptyRoom));
            Assert.Equal(LobbyStatus.Open, lifecycle.Status);
            Assert.Null(lifecycle.Notice);
        }

        [Fact]
        public void A_join_of_another_room_while_rejoining_is_ignored()
        {
            var lifecycle = Connected();
            Do(lifecycle, new AttachLobby(42));
            var step = On(lifecycle, new RoomJoined(5));
            Assert.Empty(step.Commands);
            Assert.False(step.Forward);
            Assert.Equal(LobbyStatus.Rejoining, lifecycle.Status);
        }

        [Fact]
        public void A_join_failure_for_another_room_is_ignored()
        {
            var lifecycle = Connected();
            Do(lifecycle, new AttachLobby(42));
            On(lifecycle, new RoomJoinFailed(5, "No such channel"));
            Assert.Equal(LobbyStatus.Rejoining, lifecycle.Status);
        }

        [Fact]
        public void Room_state_outside_syncing_does_not_change_the_status()
        {
            var lifecycle = OpenRoom();
            On(lifecycle, new TransportDisconnected("gone"));
            On(lifecycle, new RoomStateReceived(EmptyRoom));
            Assert.Equal(LobbyStatus.Reconnecting, lifecycle.Status);
        }
    }

    public class Reconnecting
    {
        [Theory]
        [InlineData(LobbyStatus.Open)]
        [InlineData(LobbyStatus.Syncing)]
        [InlineData(LobbyStatus.Rejoining)]
        public void A_dropped_connection_rejoins_the_same_room(LobbyStatus before)
        {
            var lifecycle = Connected();
            Do(lifecycle, new AttachLobby(7));
            if (before != LobbyStatus.Rejoining)
            {
                On(lifecycle, new RoomJoined(7));
            }

            if (before == LobbyStatus.Open)
            {
                On(lifecycle, new RoomStateReceived(EmptyRoom));
            }

            Assert.Equal(before, lifecycle.Status);
            var step = On(lifecycle, new TransportDisconnected("reset"));
            Assert.False(step.ResetEngine);
            Assert.Equal(LobbyStatus.Reconnecting, lifecycle.Status);

            step = On(lifecycle, new TransportConnected());
            Assert.Equal([new JoinRoom(7)], step.Commands);
            Assert.False(step.ResetEngine);
            Assert.Equal(LobbyStatus.Rejoining, lifecycle.Status);

            Assert.Equal([new RequestRoomState()], On(lifecycle, new RoomJoined(7)).Commands);
        }

        [Fact]
        public void A_room_that_vanished_while_away_is_made_again_with_its_title()
        {
            var lifecycle = OpenRoom();
            On(lifecycle, new TransportDisconnected("reset"));
            On(lifecycle, new TransportConnected());
            var step = On(lifecycle, new RoomJoinFailed(7, "No such channel"));
            Assert.Equal([new CreateRoom("my room")], step.Commands);
            Assert.True(step.ResetEngine);
            Assert.Equal(LobbyStatus.Creating, lifecycle.Status);
        }

        [Fact]
        public void Even_a_room_the_owner_attached_is_made_again_after_a_reconnect()
        {
            var lifecycle = Connected();
            Do(lifecycle, new AttachLobby(7));
            On(lifecycle, new RoomJoined(7));
            On(lifecycle, new RoomStateReceived(EmptyRoom));
            On(lifecycle, new TransportDisconnected("reset"));
            On(lifecycle, new TransportConnected());
            Assert.Equal([new CreateRoom("my room")], On(lifecycle, new RoomJoinFailed(7, "No such channel")).Commands);
        }

        [Fact]
        public void A_saved_session_rejoins_on_start_and_is_remade_if_gone()
        {
            var lifecycle = new LobbyLifecycle(savedRoomId: 7, savedTitle: "saved");
            Assert.Equal(LobbyStatus.Connecting, lifecycle.Status);
            Assert.Equal([new JoinRoom(7)], On(lifecycle, new TransportConnected()).Commands);
            Assert.Equal([new CreateRoom("saved")], On(lifecycle, new RoomJoinFailed(7, "gone")).Commands);
            Assert.Equal("Room 7 could not be rejoined (Bancho: gone), so a new room was made.", lifecycle.Notice);
        }

        [Fact]
        public void A_wrong_password_stops_everything_but_keeps_the_session()
        {
            var lifecycle = new LobbyLifecycle(savedRoomId: 7, savedTitle: "saved");
            On(lifecycle, new TransportAuthenticationFailed());
            Assert.Equal(LobbyStatus.Idle, lifecycle.Status);
            Assert.Equal(7, lifecycle.RoomId);
        }

        [Fact]
        public void A_pending_create_is_dropped_by_a_wrong_password()
        {
            var lifecycle = new LobbyLifecycle();
            Do(lifecycle, new CreateLobby("my room"));
            On(lifecycle, new TransportAuthenticationFailed());
            Assert.Empty(On(lifecycle, new TransportConnected()).Commands);
        }
    }

    public class Closing
    {
        [Fact]
        public void Close_sends_mp_close_and_then_forgets_the_room()
        {
            var lifecycle = OpenRoom();
            Assert.Equal([new CloseRoom()], Do(lifecycle, new CloseLobby()).Commands);
            Assert.Equal(LobbyStatus.Closing, lifecycle.Status);

            var step = On(lifecycle, new RoomCloseAcknowledged());
            Assert.True(step.ResetEngine);
            Assert.False(step.Forward);
            Assert.Equal(LobbyStatus.Idle, lifecycle.Status);
            Assert.Null(lifecycle.RoomId);

            // Bancho's PART that follows changes nothing, and nothing is made again.
            Assert.Empty(On(lifecycle, new RoomClosed(7)).Commands);
            Assert.Empty(On(lifecycle, new TransportConnected()).Commands);
        }

        [Fact]
        public void Close_while_disconnected_just_forgets_the_room()
        {
            var lifecycle = OpenRoom();
            On(lifecycle, new TransportDisconnected("gone"));
            var step = Do(lifecycle, new CloseLobby());
            Assert.Empty(step.Commands);
            Assert.True(step.ResetEngine);
            Assert.Equal(LobbyStatus.Idle, lifecycle.Status);
            Assert.Empty(On(lifecycle, new TransportConnected()).Commands);
        }

        [Fact]
        public void Close_before_anything_was_made_cancels_the_create()
        {
            var lifecycle = new LobbyLifecycle();
            Do(lifecycle, new CreateLobby("my room"));
            Do(lifecycle, new CloseLobby());
            Assert.Equal(LobbyStatus.Idle, lifecycle.Status);
            Assert.Empty(On(lifecycle, new TransportConnected()).Commands);
        }

        [Fact]
        public void A_connection_lost_while_closing_forgets_the_room()
        {
            var lifecycle = OpenRoom();
            Do(lifecycle, new CloseLobby());
            Assert.True(On(lifecycle, new TransportDisconnected("gone")).ResetEngine);
            Assert.Equal(LobbyStatus.Idle, lifecycle.Status);
            Assert.Empty(On(lifecycle, new TransportConnected()).Commands);
        }

        [Fact]
        public void A_room_closed_by_bancho_is_made_again()
        {
            var lifecycle = OpenRoom();
            var step = On(lifecycle, new RoomClosed(7));
            Assert.Equal([new CreateRoom("my room")], step.Commands);
            Assert.True(step.ResetEngine);
            Assert.Equal(LobbyStatus.Creating, lifecycle.Status);
        }

        [Fact]
        public void A_room_a_referee_closed_with_mp_close_is_left_closed()
        {
            var lifecycle = OpenRoom();
            On(lifecycle, new RoomCloseAcknowledged());
            Assert.Equal(LobbyStatus.Idle, lifecycle.Status);
        }

        [Theory]
        [InlineData("creator", "!mp close")]
        [InlineData("Creator", "  !MP CLOSE ")]
        public void The_owner_closing_in_game_is_respected_even_if_the_part_comes_first(string from, string text)
        {
            var lifecycle = OpenRoom();
            Assert.True(On(lifecycle, new ChatReceived(from, text, false)).Forward);
            Assert.Empty(On(lifecycle, new RoomClosed(7)).Commands);
            Assert.Equal(LobbyStatus.Idle, lifecycle.Status);
        }

        [Fact]
        public void A_player_typing_mp_close_does_not_count()
        {
            var lifecycle = OpenRoom();
            On(lifecycle, new ChatReceived("p1", "!mp close", false));
            Assert.Equal([new CreateRoom("my room")], On(lifecycle, new RoomClosed(7)).Commands);
            Assert.Equal("Room 7 was closed outside the dashboard, so a new room was made.", lifecycle.Notice);
        }

        [Fact]
        public void Closing_from_the_dashboard_leaves_no_notice()
        {
            var lifecycle = OpenRoom();
            Do(lifecycle, new CloseLobby());
            On(lifecycle, new RoomClosed(7));
            Assert.Null(lifecycle.Notice);
            Assert.Equal(LobbyStatus.Idle, lifecycle.Status);
        }

        [Fact]
        public void Close_when_empty_locks_the_room_and_says_so()
        {
            var lifecycle = OpenRoom();
            var step = Do(lifecycle, new CloseLobbyWhenEmpty(true));
            var password = Assert.IsType<SetPassword>(step.Commands[0]);
            Assert.False(string.IsNullOrEmpty(password.Password));
            Assert.Equal(
                ["This lobby will be closed when everyone leaves.", "Thank you for playing with the auto host rotation lobby."],
                step.Commands.OfType<Say>().Select(s => s.Text));
            Assert.True(lifecycle.ClosingWhenEmpty);

            Assert.Empty(lifecycle.AfterEngine(2).Commands);
            Assert.Equal([new CloseRoom()], lifecycle.AfterEngine(0).Commands);
            Assert.Equal(LobbyStatus.Closing, lifecycle.Status);
        }

        [Fact]
        public void The_lock_password_is_different_each_time()
        {
            var first = (SetPassword)Do(OpenRoom(), new CloseLobbyWhenEmpty(true)).Commands[0];
            var second = (SetPassword)Do(OpenRoom(), new CloseLobbyWhenEmpty(true)).Commands[0];
            Assert.NotEqual(first.Password, second.Password);
        }

        [Fact]
        public void An_empty_room_is_not_closed_unless_asked()
        {
            Assert.Empty(OpenRoom().AfterEngine(0).Commands);
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("", null)]
        [InlineData("kept", "kept")]
        public void Cancelling_close_when_empty_restores_the_password(string? kept, string? expected)
        {
            var lifecycle = OpenRoom();
            var settings = Settings with { Keeping = new RoomKeeping { Password = kept } };
            Do(lifecycle, new CloseLobbyWhenEmpty(true), settings);
            var step = Do(lifecycle, new CloseLobbyWhenEmpty(false), settings);
            Assert.Equal(new SetPassword(expected), step.Commands[0]);
            Assert.Contains(new Say("This lobby will stay open."), step.Commands);
            Assert.False(lifecycle.ClosingWhenEmpty);
            Assert.Empty(lifecycle.AfterEngine(0).Commands);
        }

        [Fact]
        public void Close_when_empty_needs_an_open_room_and_a_change()
        {
            var lifecycle = Connected();
            Assert.Empty(Do(lifecycle, new CloseLobbyWhenEmpty(true)).Commands);

            lifecycle = OpenRoom();
            Assert.Empty(Do(lifecycle, new CloseLobbyWhenEmpty(false)).Commands);
            Do(lifecycle, new CloseLobbyWhenEmpty(true));
            Assert.Empty(Do(lifecycle, new CloseLobbyWhenEmpty(true)).Commands);
        }

        [Fact]
        public void A_room_closing_when_empty_that_bancho_closes_stays_closed()
        {
            var lifecycle = OpenRoom();
            Do(lifecycle, new CloseLobbyWhenEmpty(true));
            On(lifecycle, new RoomClosed(7));
            Assert.Equal(LobbyStatus.Idle, lifecycle.Status);
        }

        [Fact]
        public void A_new_lobby_after_closing_starts_fresh()
        {
            var lifecycle = OpenRoom();
            Do(lifecycle, new CloseLobbyWhenEmpty(true));
            Do(lifecycle, new CloseLobby());
            On(lifecycle, new RoomCloseAcknowledged());

            Assert.Equal([new CreateRoom("next")], Do(lifecycle, new CreateLobby("next")).Commands);
            On(lifecycle, new RoomCreated(8, "next"));
            Assert.False(lifecycle.ClosingWhenEmpty);
            Assert.Equal([new CreateRoom("next")], On(lifecycle, new RoomClosed(8)).Commands);
        }
    }
}
