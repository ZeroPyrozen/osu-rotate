using OsuRotate.Domain;

namespace OsuRotate.Domain.Tests;

// Ported from osu-ahr's HostSkipperTest (vote parts; AFK checks are issue 03), MatchStarterTest and
// MatchAborterTest. osu-ahr waited real milliseconds; these move a manual clock instead.
public class VoteTests
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    public class SkipVotes
    {
        private readonly LobbyHarness _lobby = new(s => s with { SkipVoteCooldown = TimeSpan.Zero });

        private VoteCounter Votes => _lobby.Engine.SkipVotes;

        [Fact]
        public void Required_votes_grow_with_the_room()
        {
            int[] expected = [2, 2, 2, 2, 3, 3, 4, 4];
            Assert.Equal(2, Votes.Required);
            foreach (var required in expected)
            {
                _lobby.AddPlayers(1);
                Assert.Equal(required, Votes.Required);
            }
        }

        [Fact]
        public void The_hosts_own_skip_acts_at_once()
        {
            _lobby.AddPlayers(3);
            _lobby.Chat("p0", "!skip");
            _lobby.AssertHost("p1");
        }

        [Fact]
        public void The_hosts_skip_during_the_cooldown_is_ignored_and_says_how_long_to_wait()
        {
            var lobby = new LobbyHarness();
            lobby.AddPlayers(3);
            lobby.Time.Advance(2 * Second);
            lobby.Chat("p0", "!skip");
            lobby.AssertHost("p0");
            Assert.Contains("The host skip command is currently in cooltime. You have to wait 3.00 sec(s).", lobby.Said);
        }

        [Fact]
        public void A_longer_command_is_not_skip()
        {
            _lobby.AddPlayers(5);
            _lobby.Chat("p0", "!skipaaaaa");
            _lobby.AssertHost("p0");
        }

        [Fact]
        public void Players_skip_the_host_by_vote()
        {
            _lobby.AddPlayers(5);
            _lobby.Chat("p1", "!skip");
            Assert.Equal(1, Votes.Count);
            _lobby.Chat("p2", "!skip");
            Assert.Equal(2, Votes.Count);
            _lobby.AssertHost("p0");

            _lobby.Chat("p3", "!skip");
            _lobby.AssertHost("p1");

            _lobby.Wait(0.1 * Second);
            Assert.Contains("Passed a host skip vote: 3 / 3", _lobby.Said);
            Assert.DoesNotContain(_lobby.Said, s => s.StartsWith("Host skip progress", StringComparison.Ordinal));
        }

        [Fact]
        public void A_burst_of_votes_produces_one_progress_message()
        {
            _lobby.AddPlayers(5);
            _lobby.Chat("p1", "!skip");
            _lobby.Wait(2 * Second);
            _lobby.Chat("p2", "!skip");
            _lobby.Wait(2.9 * Second);
            Assert.Empty(_lobby.Said);

            _lobby.Wait(0.1 * Second);
            Assert.Equal(["Host skip progress: 2 / 3"], _lobby.Said);
        }

        [Fact]
        public void Votes_during_the_cooldown_are_ignored()
        {
            var lobby = new LobbyHarness();
            lobby.AddPlayers(5);
            lobby.Chat("p1", "!skip");
            lobby.Chat("p2", "!skip");
            lobby.Chat("p3", "!skip");
            Assert.Equal(0, lobby.Engine.SkipVotes.Count);

            lobby.Time.Advance(5 * Second);
            lobby.Chat("p2", "!skip");
            Assert.Equal(1, lobby.Engine.SkipVotes.Count);
        }

        [Fact]
        public void A_second_vote_from_the_same_player_does_not_count()
        {
            _lobby.AddPlayers(5);
            _lobby.Chat("p1", "!skip");
            _lobby.Chat("p2", "!skip");
            _lobby.Chat("p1", "!skip");
            Assert.Equal(2, Votes.Count);
        }

        [Fact]
        public void A_repeated_vote_after_the_progress_message_stays_silent()
        {
            _lobby.AddPlayers(5);
            _lobby.Chat("p1", "!skip");
            _lobby.Wait(5 * Second);
            _lobby.Chat("p1", "!skip");
            _lobby.Wait(5 * Second);
            Assert.Equal(["Host skip progress: 1 / 3"], _lobby.Said);
        }

        [Fact]
        public void The_next_host_can_be_voted_off_too()
        {
            _lobby.AddPlayers(5);
            _lobby.Chat("p1", "!skip");
            _lobby.Chat("p2", "!skip");
            _lobby.Chat("p3", "!skip");
            _lobby.AssertHost("p1");

            _lobby.Chat("p2", "!skip");
            _lobby.Chat("p3", "!skip");
            _lobby.Chat("p4", "!skip");
            _lobby.AssertHost("p2");
        }

        [Fact]
        public void Votes_survive_a_map_change()
        {
            _lobby.AddPlayers(5);
            _lobby.Chat("p1", "!skip");
            _lobby.Chat("p2", "!skip");
            _lobby.ChangeMap();
            _lobby.Chat("p3", "!skip");
            _lobby.AssertHost("p1");
        }

        [Fact]
        public void Votes_are_cleared_at_match_start_and_ignored_during_the_match()
        {
            _lobby.AddPlayers(5);
            _lobby.Chat("p1", "!skip");
            _lobby.Chat("p2", "!skip");
            _lobby.StartMatch();
            Assert.Equal(0, Votes.Count);

            _lobby.Chat("p3", "!skip");
            _lobby.Chat("p4", "!skip");
            Assert.Equal(0, Votes.Count);
        }

        [Fact]
        public void A_flood_of_votes_skips_once()
        {
            var lobby = new LobbyHarness();
            lobby.AddPlayers(16);
            lobby.Time.Advance(5 * Second);
            var transfersBefore = lobby.SentCount<TransferHost>();
            for (var i = 1; i < 16; i++)
            {
                lobby.Chat($"p{i}", "!skip");
            }

            Assert.Equal(transfersBefore + 1, lobby.SentCount<TransferHost>());
            lobby.AssertHost("p1");
            Assert.Equal(0, lobby.Engine.SkipVotes.Count);
        }

        [Theory]
        [InlineData("!skip p0")]
        [InlineData("!skip P0")]
        [InlineData("!skip ")]
        public void A_vote_may_name_the_host(string message)
        {
            _lobby.AddPlayers(5);
            _lobby.Chat("p1", message);
            Assert.Equal(1, Votes.Count);
        }

        [Fact]
        public void A_vote_may_name_a_host_with_spaces_and_brackets()
        {
            string[] players = ["abc xxx[aaui]", "a", "b", "c"];
            _lobby.AddPlayers(players);
            _lobby.Chat("a", "!skip abc xxx[aaui]");
            Assert.Equal(1, Votes.Count);
        }

        [Fact]
        public void A_vote_naming_someone_else_is_ignored()
        {
            _lobby.AddPlayers(5);
            _lobby.Chat("p1", "!skip abc");
            Assert.Equal(0, Votes.Count);
        }

        [Fact]
        public void Someone_outside_the_room_cannot_vote()
        {
            _lobby.AddPlayers(3);
            _lobby.Chat("stranger", "!skip");
            _lobby.Chat("p1", "!skip");
            _lobby.AssertHost("p0");
        }

        [Theory]
        [InlineData("*skip")]
        [InlineData("*skip aaa")]
        public void The_owner_skips_without_a_vote(string message)
        {
            _lobby.AddPlayers(5);
            _lobby.Chat(LobbyHarness.Owner, message);
            _lobby.AssertHost("p1");
        }

        [Fact]
        public void Votes_still_skip_after_the_host_was_cleared()
        {
            _lobby.AddPlayers(5);
            _lobby.Chat("p1", "!skip");
            _lobby.Run(() => _lobby.Engine.Handle(new HostCleared()));
            _lobby.Chat("p2", "!skip");
            Assert.Equal(2, Votes.Count);
            _lobby.Chat("p3", "!skip");
            Assert.NotNull(_lobby.Engine.Room.Host);
        }

        [Fact]
        public void A_leaving_voter_can_make_the_vote_pass()
        {
            _lobby.AddPlayers(7);
            _lobby.Chat("p1", "!skip");
            _lobby.Chat("p2", "!skip");
            _lobby.Chat("p3", "!skip");
            Assert.Equal("3 / 4", Votes.ToString());

            _lobby.RemovePlayer("p6");
            _lobby.AssertHost("p1");
        }

        // osu-ahr re-checked the vote here and skipped p1 too, though p0's leaving already moved the turn.
        [Fact]
        public void The_host_leaving_ends_the_vote_without_skipping_the_next_host()
        {
            _lobby.AddPlayers(7);
            _lobby.Chat("p1", "!skip");
            _lobby.Chat("p2", "!skip");
            _lobby.Chat("p3", "!skip");

            _lobby.RemovePlayer("p0");
            _lobby.AssertHost("p1");
            Assert.Equal(0, Votes.Count);
            _lobby.Wait(10 * Second);
            Assert.Empty(_lobby.Said);
        }
    }

    public class StartVotes
    {
        private readonly LobbyHarness _lobby = new(s => s with { StartVote = new VoteRule(0.75, 2) });

        private VoteCounter Votes => _lobby.Engine.StartVotes;

        [Fact]
        public void The_match_starts_when_everyone_is_ready()
        {
            _lobby.AddPlayers(5);
            _lobby.Feed(new AllPlayersReady());
            Assert.True(_lobby.IsMatching);
        }

        [Fact]
        public void Everyone_ready_does_nothing_when_turned_off()
        {
            var lobby = new LobbyHarness(s => s with { StartWhenAllReady = false });
            lobby.AddPlayers(5);
            lobby.Feed(new AllPlayersReady());
            Assert.False(lobby.IsMatching);
        }

        [Fact]
        public void Required_votes_grow_with_the_room()
        {
            for (var players = 1; players <= 16; players++)
            {
                _lobby.AddPlayers(1);
                Assert.Equal(Math.Max((int)Math.Ceiling(players * 0.75), 2), Votes.Required);
            }
        }

        [Fact]
        public void Players_start_the_match_by_vote()
        {
            _lobby.AddPlayers(5);
            Assert.Equal(4, Votes.Required);
            for (var i = 1; i <= 3; i++)
            {
                _lobby.Chat($"p{i}", "!start");
                Assert.False(_lobby.IsMatching);
                Assert.Equal(i, Votes.Count);
            }

            _lobby.Chat("p4", "!start");
            Assert.True(_lobby.IsMatching);
            Assert.Equal(0, Votes.Count);
        }

        [Fact]
        public void A_second_vote_from_the_same_player_does_not_count()
        {
            _lobby.AddPlayers(5);
            _lobby.Chat("p1", "!start");
            _lobby.Chat("p1", "!start");
            Assert.Equal(1, Votes.Count);
        }

        [Fact]
        public void Votes_reset_when_the_host_changes()
        {
            _lobby.AddPlayers(5);
            _lobby.Chat("p1", "!start");
            _lobby.Chat("p2", "!start");
            _lobby.Chat("p3", "!start");
            _lobby.Run(_lobby.Engine.Skip);
            _lobby.AssertHost("p1");
            Assert.Equal(0, Votes.Count);

            _lobby.Chat("p0", "!start");
            Assert.Equal(1, Votes.Count);
            _lobby.Chat("p1", "!start");
            Assert.True(_lobby.IsMatching);
        }

        [Fact]
        public void Votes_are_ignored_during_a_match()
        {
            _lobby.AddPlayers(5);
            _lobby.StartMatch();
            foreach (var player in new[] { "p0", "p1", "p2", "p3", "p4" })
            {
                _lobby.Chat(player, "!start");
                Assert.Equal(0, Votes.Count);
            }

            Assert.Equal(0, _lobby.SentCount<StartMatch>());
        }

        [Theory]
        [InlineData("!start")]
        [InlineData("!mp start")]
        public void The_host_starts_at_once(string message)
        {
            _lobby.AddPlayers(5);
            _lobby.Chat("p0", message);
            Assert.True(_lobby.IsMatching);
        }

        [Fact]
        public void The_owner_starts_without_a_vote_and_players_cannot()
        {
            _lobby.AddPlayers(5);
            _lobby.Chat("p1", "*start");
            Assert.False(_lobby.IsMatching);

            _lobby.Chat(LobbyHarness.Owner, "*start");
            Assert.True(_lobby.IsMatching);
        }

        [Fact]
        public void Start_is_sent_at_most_once_a_second()
        {
            _lobby.AddPlayers(5);
            _lobby.Holding = true;
            _lobby.Feed(new AllPlayersReady());
            _lobby.Chat("p0", "!start");
            Assert.Equal(1, _lobby.SentCount<StartMatch>());
        }

        [Fact]
        public void The_host_queues_a_start_and_hears_warnings()
        {
            _lobby.AddPlayers(5);
            _lobby.Chat("p0", "!start 40");
            Assert.False(_lobby.IsMatching);
            Assert.True(_lobby.Engine.IsStartTimerRunning);
            Assert.Contains("Queued the match to start in 40 seconds", _lobby.Said);

            _lobby.Wait(10 * Second);
            Assert.Contains("Match starts in 30 seconds", _lobby.Said);
            _lobby.Wait(20 * Second);
            Assert.Contains("Match starts in 10 seconds", _lobby.Said);
            Assert.False(_lobby.IsMatching);

            _lobby.Wait(10 * Second);
            Assert.True(_lobby.IsMatching);
            Assert.False(_lobby.Engine.IsStartTimerRunning);
        }

        [Fact]
        public void A_zero_second_start_is_immediate()
        {
            _lobby.AddPlayers(5);
            _lobby.Chat("p0", "!start 0");
            Assert.True(_lobby.IsMatching);
            Assert.False(_lobby.Engine.IsStartTimerRunning);
        }

        [Theory]
        [InlineData("!start -100")]
        [InlineData("!start -")]
        [InlineData("!start -0")]
        [InlineData("!start aaa")]
        [InlineData("!start 10 aaa")]
        [InlineData("!start aaa sdfa")]
        [InlineData("!start *231sd")]
        [InlineData("!start ٣٠")]
        [InlineData("!start 99999999999")]
        public void A_bad_countdown_is_ignored(string message)
        {
            _lobby.AddPlayers(5);
            _lobby.Chat("p0", message);
            Assert.False(_lobby.IsMatching);
            Assert.False(_lobby.Engine.IsStartTimerRunning);
            Assert.Empty(_lobby.Said);
        }

        [Fact]
        public void A_player_cannot_queue_a_start()
        {
            _lobby.AddPlayers(5);
            _lobby.Chat("p1", "!start 100");
            Assert.False(_lobby.Engine.IsStartTimerRunning);
        }

        [Fact]
        public void The_owner_can_queue_a_start_from_outside_the_room()
        {
            _lobby.AddPlayers(5);
            _lobby.Chat(LobbyHarness.Owner, "!start 30");
            Assert.True(_lobby.Engine.IsStartTimerRunning);
        }

        [Theory]
        [InlineData("!stop")]
        [InlineData("!abort")]
        public void The_host_stops_a_queued_start(string message)
        {
            _lobby.AddPlayers(5);
            _lobby.Chat("p0", "!start 30");
            _lobby.Chat("p0", message);
            Assert.False(_lobby.Engine.IsStartTimerRunning);
            Assert.Contains("Aborted the match start timer.", _lobby.Said);

            _lobby.Wait(60 * Second);
            Assert.False(_lobby.IsMatching);
            Assert.DoesNotContain("Match starts in 10 seconds", _lobby.Said);
        }

        [Fact]
        public void A_player_cannot_stop_a_queued_start()
        {
            _lobby.AddPlayers(5);
            _lobby.Chat("p0", "!start 30");
            _lobby.Chat("p1", "!stop");
            Assert.True(_lobby.Engine.IsStartTimerRunning);
        }

        [Fact]
        public void A_shorter_queued_start_replaces_the_longer_one_and_its_warnings()
        {
            _lobby.AddPlayers(5);
            _lobby.Chat("p0", "!start 60");
            _lobby.Chat("p0", "!start 5");
            _lobby.Wait(5 * Second);
            Assert.True(_lobby.IsMatching);
            _lobby.FinishMatch();

            _lobby.Wait(60 * Second);
            Assert.Equal(1, _lobby.SentCount<StartMatch>());
            Assert.DoesNotContain(_lobby.Said, s => s.StartsWith("Match starts in", StringComparison.Ordinal));
        }

        [Fact]
        public void A_shorter_queued_start_drops_a_warning_that_no_longer_fits()
        {
            _lobby.AddPlayers(5);
            _lobby.Chat("p0", "!start 40");
            _lobby.Chat("p0", "!start 30");
            _lobby.Wait(10 * Second);
            Assert.DoesNotContain("Match starts in 30 seconds", _lobby.Said);

            _lobby.Wait(10 * Second);
            Assert.Contains("Match starts in 10 seconds", _lobby.Said);
        }

        [Fact]
        public void A_host_change_stops_a_Bancho_countdown()
        {
            _lobby.AddPlayers(5);
            _lobby.Feed(new CountdownStarted(30));
            _lobby.Run(_lobby.Engine.Skip);
            Assert.Equal(1, _lobby.SentCount<AbortCountdown>());

            _lobby.Run(_lobby.Engine.Skip);
            Assert.Equal(1, _lobby.SentCount<AbortCountdown>());
        }

        [Theory]
        [InlineData(1, "1 second")]
        [InlineData(30, "30 seconds")]
        [InlineData(60, "1 minute")]
        [InlineData(90, "1 minute and 30 seconds")]
        [InlineData(121, "2 minutes and 1 second")]
        public void Countdown_text(int seconds, string text) => Assert.Equal(text, LobbyEngine.CountdownText(seconds));
    }

    public class AbortVotes
    {
        private readonly LobbyHarness _lobby = new();

        private VoteCounter Votes => _lobby.Engine.AbortVotes;

        [Fact]
        public void Required_votes_follow_the_players_who_started()
        {
            int[] expected = [2, 2, 2, 2, 2, 2, 3, 3];
            foreach (var required in expected)
            {
                _lobby.AddPlayers(1);
                _lobby.StartMatch();
                Assert.Equal(required, Votes.Required);
                _lobby.FinishMatch();
            }
        }

        [Fact]
        public void The_host_aborts_at_once()
        {
            _lobby.AddPlayers(5);
            _lobby.StartMatch();
            _lobby.Chat("p0", "!abort");
            Assert.False(_lobby.IsMatching);
            Assert.False(_lobby.Engine.Room.IsMatching);
        }

        [Fact]
        public void Players_abort_by_vote()
        {
            _lobby.AddPlayers(5);
            _lobby.StartMatch();
            _lobby.Chat("p1", "!abort");
            Assert.Equal(1, Votes.Count);
            Assert.True(_lobby.IsMatching);

            _lobby.Chat("p2", "!abort");
            Assert.False(_lobby.IsMatching);
            _lobby.Wait(0.1 * Second);
            Assert.Contains("Passed a match abort vote: 2 / 2", _lobby.Said);
        }

        [Fact]
        public void A_second_vote_from_the_same_player_does_not_count()
        {
            _lobby.AddPlayers(5);
            _lobby.StartMatch();
            _lobby.Chat("p1", "!abort");
            _lobby.Chat("p1", "!abort");
            _lobby.Chat("p1", "!abort");
            Assert.Equal(1, Votes.Count);
            Assert.True(_lobby.IsMatching);
        }

        [Fact]
        public void The_owner_aborts_without_a_vote()
        {
            _lobby.AddPlayers(5);
            _lobby.StartMatch();
            _lobby.Chat("p1", "*abort");
            Assert.True(_lobby.IsMatching);

            _lobby.Chat(LobbyHarness.Owner, "*abort");
            Assert.False(_lobby.IsMatching);
        }

        [Fact]
        public void A_leaving_player_can_make_the_vote_pass()
        {
            _lobby.AddPlayers(7);
            _lobby.StartMatch();
            Assert.Equal(3, Votes.Required);
            _lobby.Chat("p1", "!abort");
            _lobby.Chat("p2", "!abort");
            Assert.True(_lobby.IsMatching);

            _lobby.RemovePlayer("p3");
            Assert.False(_lobby.IsMatching);
        }

        [Fact]
        public void A_player_joining_during_the_match_cannot_vote()
        {
            _lobby.AddPlayers(6);
            _lobby.StartMatch();
            _lobby.Chat("p1", "!abort");
            _lobby.AddPlayers("tom");
            Assert.Equal(2, Votes.Required);
            _lobby.Chat("tom", "!abort");
            Assert.True(_lobby.IsMatching);

            _lobby.Chat("p2", "!abort");
            Assert.False(_lobby.IsMatching);
        }

        [Fact]
        public void Abort_outside_a_match_does_nothing()
        {
            _lobby.AddPlayers(5);
            _lobby.Chat("p0", "!abort");
            _lobby.Chat("p1", "!abort");
            _lobby.Chat(LobbyHarness.Owner, "*abort");
            Assert.Equal(0, _lobby.SentCount<AbortMatch>());
        }

        [Fact]
        public void Votes_from_the_last_match_do_not_carry_over()
        {
            _lobby.AddPlayers(5);
            _lobby.StartMatch();
            _lobby.Chat("p1", "!abort");
            _lobby.FinishMatch();

            _lobby.StartMatch();
            _lobby.Chat("p2", "!abort");
            Assert.True(_lobby.IsMatching);
            Assert.Equal(1, Votes.Count);
        }
    }
}
