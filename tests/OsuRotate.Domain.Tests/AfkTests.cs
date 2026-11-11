using OsuRotate.Domain;

namespace OsuRotate.Domain.Tests;

// Ported from osu-ahr's HostSkipperTest (AFK timer parts) and AfkKickerTest.
public class AfkTests
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    private static int StatRequests(LobbyHarness lobby, string name, bool viaPrivateMessage) =>
        lobby.Sent.OfType<RequestStat>().Count(r => r.Username == name && r.ViaPrivateMessage == viaPrivateMessage);

    public class HostCheck
    {
        private readonly LobbyHarness _lobby = new();

        [Fact]
        public void An_away_host_is_checked_quietly_then_publicly_and_skipped()
        {
            _lobby.AddPlayers("p1", "p2", "p3");
            _lobby.Stats["p1"] = PlayerActivity.Afk;

            _lobby.Wait(44.9 * Second);
            Assert.Equal(0, StatRequests(_lobby, "p1", viaPrivateMessage: true));

            _lobby.Wait(0.1 * Second);
            Assert.Equal(1, StatRequests(_lobby, "p1", viaPrivateMessage: true));
            Assert.Equal(1, StatRequests(_lobby, "p1", viaPrivateMessage: false));
            _lobby.AssertHost("p2");
        }

        [Theory]
        [InlineData(PlayerActivity.Idle)]
        [InlineData(PlayerActivity.Playing)]
        [InlineData(PlayerActivity.Watching)]
        public void Any_activity_outside_multiplayer_counts_as_away(PlayerActivity activity)
        {
            _lobby.AddPlayers("p1", "p2");
            _lobby.Stats["p1"] = activity;
            _lobby.Wait(45 * Second);
            _lobby.AssertHost("p2");
        }

        [Theory]
        [InlineData(PlayerActivity.Multiplayer)]
        [InlineData(PlayerActivity.Unknown)]
        public void A_present_host_keeps_the_turn_and_is_checked_every_90_seconds(PlayerActivity activity)
        {
            _lobby.AddPlayers("p1", "p2");
            _lobby.Stats["p1"] = activity;
            _lobby.Wait(45 * Second);
            _lobby.AssertHost("p1");
            Assert.Equal(0, StatRequests(_lobby, "p1", viaPrivateMessage: false));

            _lobby.Wait(89.9 * Second);
            Assert.Equal(1, StatRequests(_lobby, "p1", viaPrivateMessage: true));
            _lobby.Wait(0.1 * Second);
            Assert.Equal(2, StatRequests(_lobby, "p1", viaPrivateMessage: true));
        }

        [Fact]
        public void No_answer_means_trying_again_later()
        {
            _lobby.AddPlayers("p1", "p2");
            _lobby.Wait(45 * Second);
            _lobby.Wait(5 * Second);
            _lobby.Wait(89.9 * Second);
            Assert.Equal(1, StatRequests(_lobby, "p1", viaPrivateMessage: true));

            _lobby.Wait(0.1 * Second);
            Assert.Equal(2, StatRequests(_lobby, "p1", viaPrivateMessage: true));
        }

        [Fact]
        public void A_new_host_gets_a_fresh_first_check()
        {
            _lobby.AddPlayers("p1", "p2", "p3");
            _lobby.Stats["p2"] = PlayerActivity.Afk;
            _lobby.Wait(30 * Second);
            _lobby.Run(_lobby.Engine.Skip);
            _lobby.AssertHost("p2");

            _lobby.Wait(44 * Second);
            _lobby.AssertHost("p2");
            _lobby.Wait(1 * Second);
            _lobby.AssertHost("p3");
        }

        [Fact]
        public void Changing_the_map_restarts_the_wait()
        {
            _lobby.AddPlayers("p1", "p2");
            _lobby.Stats["p1"] = PlayerActivity.Afk;
            _lobby.Wait(40 * Second);
            _lobby.ChangeMap();

            _lobby.Wait(89 * Second);
            _lobby.AssertHost("p1");
            _lobby.Wait(1 * Second);
            _lobby.AssertHost("p2");
        }

        [Fact]
        public void The_host_chatting_restarts_the_wait()
        {
            _lobby.AddPlayers("p1", "p2");
            _lobby.Stats["p1"] = PlayerActivity.Afk;
            _lobby.Wait(40 * Second);
            _lobby.Chat("p1", "hello");

            _lobby.Wait(89 * Second);
            _lobby.AssertHost("p1");
            _lobby.Wait(1 * Second);
            _lobby.AssertHost("p2");
        }

        [Fact]
        public void A_zero_interval_turns_the_check_off()
        {
            var lobby = new LobbyHarness(s => s with { HostAfkCheckInterval = TimeSpan.Zero });
            lobby.AddPlayers("p1", "p2");
            lobby.Stats["p1"] = PlayerActivity.Afk;
            lobby.Wait(600 * Second);
            Assert.False(lobby.Engine.IsHostAfkCheckRunning);
            Assert.Equal(0, lobby.SentCount<RequestStat>());
        }

        [Fact]
        public void With_skipping_off_players_are_told_how_to_vote()
        {
            var lobby = new LobbyHarness(s => s with { SkipAfkHost = false, AfkKickEnabled = false });
            lobby.AddPlayers("p1", "p2");
            lobby.Stats["p1"] = PlayerActivity.Afk;
            lobby.Wait(45 * Second);
            lobby.AssertHost("p1");
            Assert.Contains("Players can skip the AFK host with !skip to vote.", lobby.Said);

            lobby.ChangeMap();
            lobby.Wait(90 * Second);
            Assert.Contains("Players can start the match with !start to vote.", lobby.Said);
        }

        [Fact]
        public void A_newcomer_takes_over_from_a_lone_away_host()
        {
            _lobby.AddPlayers("p1");
            _lobby.Stats["p1"] = PlayerActivity.Afk;
            _lobby.Wait(45 * Second);
            _lobby.AssertHost("p1");

            _lobby.AddPlayers("p2");
            _lobby.AssertHost("p2");
        }

        [Fact]
        public void A_newcomer_does_not_take_over_once_the_away_report_is_old()
        {
            _lobby.AddPlayers("p1");
            _lobby.Stats["p1"] = PlayerActivity.Afk;
            _lobby.Wait(45 * Second);
            _lobby.Stats["p1"] = PlayerActivity.Multiplayer;
            _lobby.Time.Advance(90 * Second);

            _lobby.AddPlayers("p2");
            _lobby.AssertHost("p1");
        }

        [Fact]
        public void There_is_no_check_during_a_match()
        {
            _lobby.AddPlayers("p1", "p2");
            _lobby.Stats["p1"] = PlayerActivity.Afk;
            _lobby.StartMatch();
            Assert.False(_lobby.Engine.IsHostAfkCheckRunning);
            _lobby.Wait(45 * Second);
            Assert.Equal(0, StatRequests(_lobby, "p1", viaPrivateMessage: true));
        }

        [Fact]
        public void A_late_private_answer_does_not_skip()
        {
            _lobby.AddPlayers("p1", "p2");
            _lobby.Wait(50 * Second);
            _lobby.Feed(new PlayerStatReceived(new PlayerStat("p1", 1, PlayerActivity.Afk, 0, 0, 0, 0, 0), ViaPrivateMessage: true));
            _lobby.AssertHost("p1");
            Assert.Equal(0, StatRequests(_lobby, "p1", viaPrivateMessage: false));
        }

        [Fact]
        public void A_cleared_host_is_not_checked()
        {
            _lobby.AddPlayers("p1", "p2");
            _lobby.Run(() => _lobby.Engine.Handle(new HostCleared()));
            _lobby.Wait(45 * Second);
            Assert.Equal(0, StatRequests(_lobby, "p1", viaPrivateMessage: true));
        }

        [Fact]
        public void A_public_stat_only_skips_an_away_host()
        {
            _lobby.AddPlayers("p1", "p2", "p3");
            _lobby.PublicStat("p2", PlayerActivity.Afk);
            _lobby.PublicStat("p1", PlayerActivity.Multiplayer);
            _lobby.AssertHost("p1");

            _lobby.PublicStat("p1", PlayerActivity.Idle);
            _lobby.AssertHost("p2");
        }
    }

    public class Kicker
    {
        private readonly LobbyHarness _lobby = new(s => s with { AfkKickCooldown = TimeSpan.Zero, HostAfkCheckInterval = TimeSpan.Zero });

        private int Points(string name) => _lobby.Engine.AfkPoints(name);

        [Fact]
        public void An_away_stat_adds_3_points()
        {
            _lobby.AddPlayers("p1", "p2");
            _lobby.PublicStat("p1", PlayerActivity.Afk);
            _lobby.PublicStat("p2", PlayerActivity.Multiplayer);
            Assert.Equal(3, Points("p1"));
            Assert.Equal(0, Points("p2"));
        }

        [Fact]
        public void Finishing_without_a_score_adds_2_and_scoring_clears_them()
        {
            _lobby.AddPlayers("p1", "p2");
            _lobby.StartMatch();
            _lobby.FinishMatchWith(("p1", 0), ("p2", 100));
            Assert.Equal(2, Points("p1"));
            Assert.Equal(0, Points("p2"));

            _lobby.StartMatch();
            _lobby.FinishMatchWith(("p1", 100), ("p2", 100));
            Assert.Equal(0, Points("p1"));
        }

        [Fact]
        public void Not_finishing_at_all_adds_2()
        {
            _lobby.AddPlayers("p1", "p2");
            _lobby.StartMatch();
            _lobby.FinishMatchWith(("p2", 100));
            Assert.Equal(2, Points("p1"));
            Assert.Equal(0, Points("p2"));
        }

        [Fact]
        public void Chatting_takes_a_point_away_down_to_zero()
        {
            _lobby.AddPlayers("p1", "p2");
            _lobby.PublicStat("p1", PlayerActivity.Afk);
            for (var expected = 2; expected >= 0; expected--)
            {
                _lobby.Chat("p1", "hello");
                Assert.Equal(expected, Points("p1"));
            }

            _lobby.Chat("p1", "hello");
            Assert.Equal(0, Points("p1"));
        }

        [Fact]
        public void Six_points_kick_the_player()
        {
            _lobby.AddPlayers("p1", "p2");
            _lobby.PublicStat("p1", PlayerActivity.Afk);
            Assert.Contains("p1", _lobby.Players);
            _lobby.PublicStat("p1", PlayerActivity.Afk);

            Assert.DoesNotContain("p1", _lobby.Players);
            Assert.Contains("p2", _lobby.Players);
            Assert.Null(_lobby.Engine.Room.Find("p1") is { } p1 && _lobby.Engine.Room.Contains(p1) ? p1 : null);
            Assert.Contains("Kicked an AFK player.", _lobby.Said);
        }

        [Fact]
        public void Points_change_at_most_once_per_cooldown_counted_from_joining()
        {
            var lobby = new LobbyHarness(s => s with { AfkKickThreshold = 100, HostAfkCheckInterval = TimeSpan.Zero });
            lobby.AddPlayers("p1", "p2");
            lobby.PublicStat("p1", PlayerActivity.Afk);
            Assert.Equal(0, lobby.Engine.AfkPoints("p1"));

            lobby.Time.Advance(30 * Second);
            lobby.PublicStat("p1", PlayerActivity.Afk);
            lobby.PublicStat("p1", PlayerActivity.Afk);
            Assert.Equal(3, lobby.Engine.AfkPoints("p1"));

            lobby.Time.Advance(29 * Second);
            lobby.PublicStat("p1", PlayerActivity.Afk);
            Assert.Equal(3, lobby.Engine.AfkPoints("p1"));

            lobby.Time.Advance(1 * Second);
            lobby.PublicStat("p1", PlayerActivity.Afk);
            Assert.Equal(6, lobby.Engine.AfkPoints("p1"));
        }

        [Fact]
        public void Nothing_counts_while_disabled()
        {
            var lobby = new LobbyHarness(s => s with { AfkKickEnabled = false, AfkKickCooldown = TimeSpan.Zero });
            lobby.AddPlayers("p1", "p2");
            lobby.PublicStat("p1", PlayerActivity.Afk);
            lobby.PublicStat("p1", PlayerActivity.Afk);
            Assert.Equal(0, lobby.Engine.AfkPoints("p1"));
            Assert.Equal(0, lobby.SentCount<KickPlayer>());
        }

        [Fact]
        public void The_owner_is_never_kicked()
        {
            _lobby.AddPlayers(LobbyHarness.Owner, "p2");
            _lobby.PublicStat(LobbyHarness.Owner, PlayerActivity.Afk);
            _lobby.PublicStat(LobbyHarness.Owner, PlayerActivity.Afk);
            _lobby.StartMatch();
            _lobby.FinishMatchWith(("p2", 100));
            Assert.Equal(0, Points(LobbyHarness.Owner));
            Assert.Equal(0, _lobby.SentCount<KickPlayer>());
        }

        [Fact]
        public void An_aborted_match_gives_no_points()
        {
            _lobby.AddPlayers("p1", "p2");
            _lobby.PlayMatchAndAbort();
            _lobby.StartMatch();
            _lobby.FinishMatchWith(("p1", 100), ("p2", 100));
            Assert.Equal(0, Points("p1"));
            Assert.Equal(0, Points("p2"));
        }

        [Fact]
        public void A_player_who_joined_during_the_match_gets_no_points_for_it()
        {
            _lobby.AddPlayers("p1", "p2");
            _lobby.StartMatch();
            _lobby.AddPlayers("p3");
            _lobby.FinishMatchWith(("p1", 100), ("p2", 100));
            Assert.Equal(0, Points("p3"));
        }

        [Fact]
        public void The_owner_changes_the_rules_from_chat()
        {
            var settings = () => _lobby.Engine.Settings;
            _lobby.Chat(LobbyHarness.Owner, "*afkkick_disable");
            Assert.False(settings().AfkKickEnabled);
            _lobby.Chat(LobbyHarness.Owner, "*afkkick_enable");
            Assert.True(settings().AfkKickEnabled);

            _lobby.Chat(LobbyHarness.Owner, "*afkkick_threshold 100");
            Assert.Equal(100, settings().AfkKickThreshold);
            _lobby.Chat(LobbyHarness.Owner, "*afkkick_threshold 0");
            Assert.Equal(1, settings().AfkKickThreshold);
            _lobby.Chat(LobbyHarness.Owner, "*afkkick_threshold abc");
            Assert.Equal(1, settings().AfkKickThreshold);

            _lobby.Chat(LobbyHarness.Owner, "*afkkick_cooltime 100");
            Assert.Equal(100 * Second, settings().AfkKickCooldown);
            _lobby.Chat(LobbyHarness.Owner, "*afkkick_cooltime 0");
            Assert.Equal(10 * Second, settings().AfkKickCooldown);
        }

        [Fact]
        public void Players_cannot_change_the_rules()
        {
            _lobby.AddPlayers("p1");
            _lobby.Chat("p1", "*afkkick_disable");
            _lobby.Chat("p1", "*afkkick_threshold 100");
            Assert.True(_lobby.Engine.Settings.AfkKickEnabled);
            Assert.Equal(6, _lobby.Engine.Settings.AfkKickThreshold);
        }
    }
}
