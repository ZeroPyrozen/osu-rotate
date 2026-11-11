using OsuRotate.Domain;

namespace OsuRotate.Domain.Tests;

// Ported from osu-ahr's AutoStartTimerTest and MatchAborterTest's auto abort tests.
public class AutoStartTests
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    public class AutoStart
    {
        private readonly LobbyHarness _lobby = Create(clearHost: true);

        private static LobbyHarness Create(bool enabled = true, bool clearHost = false, int seconds = 60)
        {
            var lobby = new LobbyHarness(s => s with
            {
                AutoStartEnabled = enabled,
                AutoStartClearsHost = clearHost,
                AutoStartDelay = TimeSpan.FromSeconds(seconds),
                HostAfkCheckInterval = TimeSpan.Zero,
            });
            lobby.AddPlayers(3);
            return lobby;
        }

        [Fact]
        public void A_new_map_queues_a_start_and_clears_the_host()
        {
            _lobby.ChangeMap();
            Assert.Contains("Queued the match to start in 1 minute. (Host can stop the timer with !stop command.)", _lobby.Said);
            Assert.Equal(1, _lobby.SentCount<ClearHost>());
            Assert.True(_lobby.Engine.Room.IsHostCleared);

            _lobby.Wait(59 * Second);
            Assert.False(_lobby.IsMatching);
            _lobby.Wait(1 * Second);
            Assert.True(_lobby.IsMatching);
        }

        [Fact]
        public void The_host_can_be_kept()
        {
            var lobby = Create(clearHost: false);
            lobby.ChangeMap();
            Assert.True(lobby.Engine.IsStartTimerRunning);
            Assert.Equal(0, lobby.SentCount<ClearHost>());
        }

        [Fact]
        public void Nothing_happens_when_disabled()
        {
            var lobby = Create(enabled: false);
            lobby.ChangeMap();
            Assert.False(lobby.Engine.IsStartTimerRunning);
            Assert.Equal(0, lobby.SentCount<ClearHost>());
            Assert.Empty(lobby.Said);
        }

        [Fact]
        public void A_delay_under_15_seconds_is_not_used()
        {
            var lobby = Create(seconds: 10);
            lobby.ChangeMap();
            Assert.False(lobby.Engine.IsStartTimerRunning);
        }

        [Fact]
        public void Another_map_restarts_the_wait()
        {
            var lobby = Create();
            lobby.ChangeMap();
            lobby.Wait(30 * Second);
            lobby.ChangeMap();
            lobby.Wait(59 * Second);
            Assert.False(lobby.IsMatching);
            lobby.Wait(1 * Second);
            Assert.True(lobby.IsMatching);
        }

        [Fact]
        public void A_host_change_cancels_the_wait()
        {
            var lobby = Create();
            lobby.ChangeMap();
            lobby.Run(lobby.Engine.Skip);
            Assert.False(lobby.Engine.IsStartTimerRunning);
            lobby.Wait(120 * Second);
            Assert.False(lobby.IsMatching);
        }

        [Fact]
        public void A_lone_player_starts_when_they_like()
        {
            var lobby = new LobbyHarness(s => s with { AutoStartEnabled = true, AutoStartDelay = TimeSpan.FromSeconds(60) });
            lobby.AddPlayers(1);
            lobby.ChangeMap();
            Assert.False(lobby.Engine.IsStartTimerRunning);
        }

        [Fact]
        public void The_map_just_played_does_not_start_itself_again()
        {
            var lobby = Create();
            lobby.Feed(new BeatmapChanged(42, "map", false));
            lobby.Wait(60 * Second);
            lobby.FinishMatch();

            lobby.Feed(new BeatmapChanging());
            lobby.Feed(new BeatmapChanged(42, "map", false));
            Assert.False(lobby.Engine.IsStartTimerRunning);
        }

        [Fact]
        public void A_map_change_stops_a_Bancho_countdown()
        {
            var lobby = Create();
            lobby.Feed(new CountdownStarted(30));
            lobby.ChangeMap();
            Assert.Equal(1, lobby.SentCount<AbortCountdown>());
        }

        [Fact]
        public void The_owner_changes_the_rules_from_chat()
        {
            var lobby = Create(clearHost: false);
            var settings = () => lobby.Engine.Settings;
            lobby.Chat(LobbyHarness.Owner, "*autostart_disable");
            Assert.False(settings().AutoStartEnabled);
            lobby.Chat(LobbyHarness.Owner, "*autostart_enable");
            Assert.True(settings().AutoStartEnabled);
            lobby.Chat(LobbyHarness.Owner, "*autostart_clearhost_enable");
            Assert.True(settings().AutoStartClearsHost);
            lobby.Chat(LobbyHarness.Owner, "*autostart_clearhost_disable");
            Assert.False(settings().AutoStartClearsHost);
            lobby.Chat(LobbyHarness.Owner, "*autostart_time 50");
            Assert.Equal(50 * Second, settings().AutoStartDelay);
            lobby.Chat(LobbyHarness.Owner, "*autostart_time -50");
            Assert.Equal(15 * Second, settings().AutoStartDelay);

            lobby.Chat("p1", "*autostart_disable");
            Assert.True(settings().AutoStartEnabled);
        }
    }

    public class StuckMatch
    {
        private readonly LobbyHarness _lobby = new(s => s with { HostAfkCheckInterval = TimeSpan.Zero });

        private void Finish(params string[] names)
        {
            foreach (var name in names)
            {
                _lobby.Feed(new PlayerFinished(name, 1000, true));
            }
        }

        [Fact]
        public void A_match_nobody_is_still_playing_is_aborted()
        {
            _lobby.AddPlayers(5);
            _lobby.StartMatch();
            Finish("p0", "p1");
            Assert.False(_lobby.Engine.IsStuckMatchCheckRunning);
            Finish("p2");
            Assert.True(_lobby.Engine.IsStuckMatchCheckRunning);
            _lobby.Stats["p3"] = PlayerActivity.Idle;
            _lobby.Stats["p4"] = PlayerActivity.Multiplayer;

            _lobby.Wait(29.9 * Second);
            Assert.Equal(0, _lobby.SentCount<RequestStat>());
            _lobby.Wait(0.1 * Second);
            Assert.Equal(["p3", "p4"], _lobby.Sent.OfType<RequestStat>().Select(r => r.Username));
            Assert.False(_lobby.IsMatching);
        }

        [Fact]
        public void No_answer_counts_as_stuck()
        {
            _lobby.AddPlayers(5);
            _lobby.StartMatch();
            Finish("p0", "p1", "p2");
            _lobby.Wait(30 * Second);
            Assert.True(_lobby.IsMatching);
            _lobby.Wait(5 * Second);
            Assert.False(_lobby.IsMatching);
        }

        [Fact]
        public void Someone_still_playing_keeps_the_match_and_checks_again_later()
        {
            _lobby.AddPlayers(5);
            _lobby.StartMatch();
            Finish("p0", "p1", "p2");
            _lobby.Stats["p3"] = PlayerActivity.Multiplaying;
            _lobby.Stats["p4"] = PlayerActivity.Idle;
            _lobby.Wait(30 * Second);
            Assert.True(_lobby.IsMatching);
            Assert.Equal(2, _lobby.SentCount<RequestStat>());

            _lobby.Wait(30 * Second);
            Assert.Equal(4, _lobby.SentCount<RequestStat>());
            _lobby.Stats["p3"] = PlayerActivity.Idle;
            _lobby.Wait(30 * Second);
            Assert.False(_lobby.IsMatching);
        }

        [Fact]
        public void A_match_that_finishes_normally_is_left_alone()
        {
            _lobby.AddPlayers(5);
            _lobby.StartMatch();
            Finish("p0", "p1", "p2");
            _lobby.FinishMatch();
            Assert.False(_lobby.Engine.IsStuckMatchCheckRunning);
            _lobby.Wait(60 * Second);
            Assert.Equal(0, _lobby.SentCount<AbortMatch>());
        }

        [Fact]
        public void A_player_leaving_can_start_the_check()
        {
            _lobby.AddPlayers(5);
            _lobby.StartMatch();
            Finish("p0", "p1");
            Assert.False(_lobby.Engine.IsStuckMatchCheckRunning);
            _lobby.RemovePlayer("p2");
            Assert.True(_lobby.Engine.IsStuckMatchCheckRunning);
        }

        [Fact]
        public void A_player_joining_during_the_match_does_not_count()
        {
            _lobby.AddPlayers(6);
            _lobby.StartMatch();
            _lobby.AddPlayers("tom");
            Finish("p0", "p1");
            Assert.False(_lobby.Engine.IsStuckMatchCheckRunning);
            Finish("p2");
            Assert.True(_lobby.Engine.IsStuckMatchCheckRunning);

            _lobby.Wait(35 * Second);
            Assert.DoesNotContain(_lobby.Sent.OfType<RequestStat>(), r => r.Username == "tom");
        }

        [Fact]
        public void With_aborting_off_players_are_told_how_to_vote()
        {
            var lobby = new LobbyHarness(s => s with { AbortStuckMatch = false, HostAfkCheckInterval = TimeSpan.Zero });
            lobby.AddPlayers(2);
            lobby.StartMatch();
            lobby.Feed(new PlayerFinished("p0", 1000, true));
            lobby.Wait(35 * Second);
            Assert.True(lobby.IsMatching);
            Assert.Contains("If the match is stuck, abort the match with !abort to vote.", lobby.Said);
        }

        [Fact]
        public void A_zero_delay_turns_the_check_off()
        {
            var lobby = new LobbyHarness(s => s with { StuckMatchDelay = TimeSpan.Zero });
            lobby.AddPlayers(2);
            lobby.StartMatch();
            lobby.Feed(new PlayerFinished("p0", 1000, true));
            Assert.False(lobby.Engine.IsStuckMatchCheckRunning);
        }
    }
}
