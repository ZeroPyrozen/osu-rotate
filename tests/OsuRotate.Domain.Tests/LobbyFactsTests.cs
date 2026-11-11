using OsuRotate.Domain;

namespace OsuRotate.Domain.Tests;

// The cases from osu-ahr's LobbyTest the rotation port did not already cover: who is in the room, the
// 16 player limit, contradictions that make the bot ask for !mp settings, match status counts and
// referees. Its message, stat and settings-load tests are covered by the Bancho layer, the AFK tests
// and HostRotationTests.RoomStates_; its abort counting tests by HostRotationTests.Aborts.
public class LobbyFactsTests
{
    private static LobbyHarness Create(params string[] players)
    {
        var lobby = new LobbyHarness(s => s with { HostAfkCheckInterval = TimeSpan.Zero });
        lobby.AddPlayers(players.Length == 0 ? ["user1", "user 2", "user_3"] : players);
        lobby.Sent.Clear();
        return lobby;
    }

    private static IEnumerable<string> Present(LobbyHarness lobby) => lobby.Engine.Room.Players.Select(p => p.Name);

    public class JoinAndLeave
    {
        [Fact]
        public void Joined_players_are_in_the_room_as_plain_players()
        {
            var lobby = Create();
            Assert.Equal(["user1", "user 2", "user_3"], Present(lobby));
            foreach (var player in lobby.Engine.Room.Players)
            {
                Assert.Equal(MatchStatus.InLobby, player.Status);
                Assert.Equal(Team.None, player.Team);
                Assert.False(lobby.Engine.Room.IsReferee(player.Name));
            }
        }

        [Fact]
        public void A_player_who_left_is_gone()
        {
            var lobby = Create();
            lobby.RemovePlayer("user 2");
            Assert.Equal(["user1", "user_3"], Present(lobby));
        }

        [Fact]
        public void Players_can_come_back()
        {
            var lobby = Create("user1");
            lobby.RemovePlayer("user1");
            Assert.Empty(Present(lobby));
            lobby.AddPlayers("user1", "user 2", "user_3");
            lobby.RemovePlayer("user 2");
            Assert.Equal(["user1", "user_3"], Present(lobby));
        }

        // osu-ahr bug reproduction: chat arrives with underscores for a name that joined with spaces.
        [Fact]
        public void A_name_with_spaces_and_its_irc_form_are_the_same_player()
        {
            var lobby = Create("p1");
            lobby.Feed(new PlayerJoined("Omen de cobra", 2, Team.None));
            Assert.Same(lobby.Engine.Room.Find("Omen de cobra"), lobby.Engine.Room.Find("Omen_de_cobra"));
            Assert.Same(lobby.Engine.Room.Find("Omen de cobra"), lobby.Engine.Room.Find("OMEN DE COBRA"));

            lobby.Chat("Omen_de_cobra", "!q");
            Assert.NotEmpty(lobby.Said);
        }

        [Fact]
        public void The_owner_joining_is_a_player_but_keeps_owner_commands()
        {
            var lobby = Create("p1");
            lobby.AddPlayers(LobbyHarness.Owner);
            Assert.Contains(LobbyHarness.Owner, Present(lobby));
            Assert.Equal("p1", lobby.Engine.Room.Host?.Name);

            lobby.Chat(LobbyHarness.Owner, "*skip");
            Assert.Equal(LobbyHarness.Owner, lobby.Engine.Room.Host?.Name);
        }

        [Fact]
        public void The_pending_host_leaving_cancels_the_transfer()
        {
            var lobby = Create();
            lobby.Holding = true;
            lobby.Engine.Skip();
            lobby.Feed(new PlayerLeft("user 2"));
            Assert.Null(lobby.Engine.Room.PendingHost);
            Assert.DoesNotContain("user 2", Present(lobby));
        }
    }

    // osu-ahr raised UnexpectedAction for these. The bot instead asks Bancho for the truth.
    public class Contradictions
    {
        [Fact]
        public void A_stranger_leaving_asks_for_the_room_state()
        {
            var lobby = Create();
            lobby.Holding = true;
            lobby.Feed(new PlayerLeft("unknown player"));
            Assert.Equal([new RequestRoomState()], lobby.Sent);
            Assert.Equal(3, lobby.Engine.Room.Players.Count);
        }

        [Fact]
        public void Joining_twice_asks_for_the_room_state()
        {
            var lobby = Create("tom", "jim");
            lobby.Holding = true;
            lobby.Feed(new PlayerJoined("tom", 3, Team.None));
            Assert.Equal([new RequestRoomState()], lobby.Sent);
            Assert.Equal(["tom", "jim"], Present(lobby));
        }

        [Fact]
        public void A_seventeenth_player_is_not_added()
        {
            var lobby = Create();
            lobby.AddPlayers(13);
            Assert.Equal(Room.MaxPlayers, lobby.Engine.Room.Players.Count);
            lobby.Sent.Clear();
            lobby.Holding = true;

            lobby.Feed(new PlayerJoined("p17", 16, Team.None));
            Assert.Equal(Room.MaxPlayers, lobby.Engine.Room.Players.Count);
            Assert.DoesNotContain("p17", Present(lobby));
            Assert.Equal([new RequestRoomState()], lobby.Sent);
        }

        [Fact]
        public void Host_given_to_someone_outside_the_room_asks_for_the_room_state()
        {
            var lobby = Create();
            lobby.Holding = true;
            lobby.Feed(new HostChanged("ghost"));
            Assert.Equal("user1", lobby.Engine.Room.Host?.Name);
            Assert.Equal([new RequestRoomState()], lobby.Sent);
        }

        [Fact]
        public void A_stranger_finishing_asks_for_the_room_state()
        {
            var lobby = Create();
            lobby.Feed(new MatchStarted());
            lobby.Sent.Clear();
            lobby.Holding = true;
            lobby.Feed(new PlayerFinished("ghost", 1000, true));
            Assert.Equal([new RequestRoomState()], lobby.Sent);
        }

        [Fact]
        public void Room_state_requests_have_a_cooldown()
        {
            var lobby = Create();
            lobby.Holding = true;
            lobby.Feed(new PlayerLeft("ghost1"));
            lobby.Feed(new PlayerLeft("ghost2"));
            lobby.Time.Advance(TimeSpan.FromSeconds(14));
            lobby.Feed(new HostChanged("ghost3"));
            Assert.Equal(1, lobby.SentCount<RequestRoomState>());

            lobby.Time.Advance(TimeSpan.FromSeconds(1));
            lobby.Feed(new PlayerLeft("ghost4"));
            Assert.Equal(2, lobby.SentCount<RequestRoomState>());
        }

        [Fact]
        public void The_room_state_answer_puts_the_room_right()
        {
            var lobby = Create();
            lobby.AddPlayers("tom");
            lobby.Feed(new PlayerJoined("tom", 4, Team.None));
            lobby.Feed(new PlayerLeft("ghost"));
            Assert.Equal(1, lobby.SentCount<RequestRoomState>());
            Assert.Equal(["user1", "user 2", "user_3", "tom"], Present(lobby));
            Assert.Equal("user1", lobby.Engine.Room.Host?.Name);
        }
    }

    public class MatchStatuses
    {
        [Fact]
        public void Statuses_follow_the_match()
        {
            var lobby = Create("p0", "p1", "p2", "p3", "p4");
            var room = lobby.Engine.Room;
            int Count(MatchStatus status) => room.Players.Count(p => p.Status == status);

            lobby.RemovePlayer("p4");
            Assert.Equal(4, Count(MatchStatus.InLobby));

            lobby.Feed(new MatchStarted());
            lobby.AddPlayers("p5");
            Assert.Equal((1, 4), (Count(MatchStatus.InLobby), Count(MatchStatus.Playing)));

            lobby.RemovePlayer("p3");
            Assert.Equal((1, 3), (Count(MatchStatus.InLobby), Count(MatchStatus.Playing)));

            lobby.Feed(new PlayerFinished("p0", 1000, true));
            Assert.Equal((1, 2, 1), (Count(MatchStatus.InLobby), Count(MatchStatus.Playing), Count(MatchStatus.Finished)));

            lobby.Feed(new MatchFinished());
            Assert.Equal(4, Count(MatchStatus.InLobby));
            Assert.False(room.IsMatching);
        }

        [Fact]
        public void A_player_who_left_mid_match_has_no_status()
        {
            var lobby = Create();
            lobby.Feed(new MatchStarted());
            var left = lobby.Engine.Room.Find("user 2")!;
            lobby.RemovePlayer("user 2");
            Assert.Equal(MatchStatus.None, left.Status);
        }
    }

    public class Referees
    {
        [Fact]
        public void Referees_added_and_removed_are_tracked()
        {
            var lobby = Create();
            lobby.Feed(new RefereeAdded("user 2"));
            lobby.Feed(new RefereeAdded("outsider"));
            Assert.True(lobby.Engine.Room.IsReferee("user_2"));
            Assert.True(lobby.Engine.Room.IsReferee("Outsider"));

            lobby.Feed(new RefereeRemoved("user_2"));
            Assert.False(lobby.Engine.Room.IsReferee("user 2"));
            Assert.Single(lobby.Engine.Room.Referees);
        }

        [Fact]
        public void A_referee_list_replaces_what_was_known()
        {
            var lobby = Create();
            lobby.Feed(new RefereeAdded("old"));
            lobby.Feed(new RefereeListStarted());
            lobby.Feed(new RefereeAdded("creator"));
            lobby.Feed(new RefereeAdded("user1"));
            Assert.Equal(["creator", "user1"], lobby.Engine.Room.Referees.Order());
        }

        [Fact]
        public void Joining_an_existing_room_asks_for_its_referees()
        {
            var lobby = Create();
            lobby.Feed(new RoomJoined(1));
            Assert.Contains(new RequestReferees(), lobby.Sent);
        }

        [Fact]
        public void A_new_room_has_no_other_referees_to_ask_about()
        {
            var lobby = Create();
            lobby.Feed(new RoomCreated(1, "room"));
            Assert.DoesNotContain(new RequestReferees(), lobby.Sent);
        }

        [Fact]
        public void A_referees_mp_command_is_left_to_bancho()
        {
            var lobby = Create();
            lobby.Feed(new RefereeAdded("user 2"));
            lobby.Chat("user 2", "!mp start");
            lobby.Chat("user_3", "!mp start");
            Assert.Equal(1, lobby.Engine.StartVotes.Count);
        }

        [Fact]
        public void A_referees_ordinary_commands_still_count()
        {
            var lobby = Create();
            lobby.Feed(new RefereeAdded("user 2"));
            lobby.Chat("user 2", "!start");
            Assert.Equal(1, lobby.Engine.StartVotes.Count);
        }
    }
}
