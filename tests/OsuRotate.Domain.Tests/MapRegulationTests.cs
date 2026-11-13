using OsuRotate.Domain;

namespace OsuRotate.Domain.Tests;

// Ported from osu-ahr's MapCheckerTest. Option validation from config files moves to the settings
// layer (phase 4 and 5); the owner command, description, check and skip tests are here.
public class MapRegulationTests
{
    private static MapRegulation Apply(MapRegulation current, string message)
    {
        var command = ChatCommand.Parse(message)!;
        return RegulationCommand.Apply(current, command.Name, command.Argument) ?? current;
    }

    public class OwnerCommands
    {
        [Theory]
        [InlineData("*regulation enabled", true)]
        [InlineData("*regulation disabled", false)]
        [InlineData("*regulation enable", true)]
        [InlineData("*regulation disable", false)]
        [InlineData("*mapchecker_enable", true)]
        [InlineData("*mapchecker_disable", false)]
        [InlineData("*no regulation", false)]
        public void Enabled(string message, bool expected)
        {
            Assert.Equal(expected, Apply(new MapRegulation { Enabled = !expected }, message).Enabled);
        }

        [Theory]
        [InlineData("*regulation num_violations_allowed 3", 3)]
        [InlineData("*regulation num_violations_allowed 10", 10)]
        [InlineData("*regulation num_violations_to_skip 5", 5)]
        [InlineData("*regulation num_violations_allowed 0", 0)]
        [InlineData("*regulation num_violations_allowed", 1)]
        [InlineData("*regulation num_violations_allowed asf", 1)]
        [InlineData("*regulation num_violations_allowed NaN", 1)]
        [InlineData("*regulation num_violations_to_skip", 1)]
        [InlineData("*no regulation num_violations_allowed", 0)]
        public void Violations_allowed(string message, int expected)
        {
            Assert.Equal(expected, Apply(new MapRegulation { ViolationsAllowed = 1 }, message).ViolationsAllowed);
        }

        [Theory]
        [InlineData(1, 5, "*regulation star_min 3", 3, 5)]
        [InlineData(1, 5, "*regulation star_min 0", 0, 5)]
        [InlineData(1, 5, "*regulation star_min 10", 10, 0)]
        [InlineData(1, 5, "*regulation    starmin  5   ", 5, 0)]
        [InlineData(1, 5, "*regulation difflow 3", 3, 5)]
        [InlineData(1, 5, "*regulation star_min -3", 1, 5)]
        [InlineData(1, 5, "*regulation star_min", 1, 5)]
        [InlineData(1, 5, "*regulation star_min   ", 1, 5)]
        [InlineData(1, 5, "*regulation star_min a", 1, 5)]
        [InlineData(3, 5, "*regulation star_max 4", 3, 4)]
        [InlineData(3, 5, "*regulation star_max 0", 3, 0)]
        [InlineData(3, 5, "*regulation star_max 10", 3, 10)]
        [InlineData(3, 5, "*regulation star_max 2", 0, 2)]
        [InlineData(3, 5, "*regulation    starmax  5   ", 3, 5)]
        [InlineData(3, 5, "*regulation diffupperlimit 3", 0, 3)]
        [InlineData(3, 5, "*regulation star_max -3", 3, 5)]
        [InlineData(3, 5, "*regulation star_max a", 3, 5)]
        [InlineData(3, 5, "*no regulation star_max", 3, 0)]
        [InlineData(3, 5, "*no regulation star_min", 0, 5)]
        public void Star_limits(double min, double max, string message, double expectedMin, double expectedMax)
        {
            var result = Apply(new MapRegulation { StarMin = min, StarMax = max }, message);
            Assert.Equal(expectedMin, result.StarMin);
            Assert.Equal(expectedMax, result.StarMax);
        }

        [Theory]
        [InlineData(1, 5, "*regulation length_min 3", 3, 5)]
        [InlineData(1, 5, "*regulation length_min 0", 0, 5)]
        [InlineData(1, 5, "*regulation length_min 10", 10, 0)]
        [InlineData(1, 5, "*regulation    lenmin  5   ", 5, 0)]
        [InlineData(1, 5, "*regulation lenlower 3", 3, 5)]
        [InlineData(1, 5, "*regulation length_min -3", 1, 5)]
        [InlineData(1, 5, "*regulation length_min a", 1, 5)]
        [InlineData(3, 5, "*regulation length_max 4", 3, 4)]
        [InlineData(3, 5, "*regulation length_max 0", 3, 0)]
        [InlineData(3, 5, "*regulation length_max 10", 3, 10)]
        [InlineData(3, 5, "*regulation length_max 2", 0, 2)]
        [InlineData(3, 5, "*regulation lenupperlimit 3", 0, 3)]
        [InlineData(3, 5, "*regulation length_max", 3, 5)]
        [InlineData(3, 5, "*no regulation length_max", 3, 0)]
        public void Length_limits(int min, int max, string message, int expectedMin, int expectedMax)
        {
            var result = Apply(new MapRegulation { LengthMinSeconds = min, LengthMaxSeconds = max }, message);
            Assert.Equal(expectedMin, result.LengthMinSeconds);
            Assert.Equal(expectedMax, result.LengthMaxSeconds);
        }

        [Theory]
        [InlineData(GameMode.Mania, "*regulation gamemode osu", GameMode.Osu)]
        [InlineData(GameMode.Mania, "*regulation gamemode Osu", GameMode.Osu)]
        [InlineData(GameMode.Mania, "*regulation gamemode 0", GameMode.Osu)]
        [InlineData(GameMode.Mania, "*regulation gamemode taiko", GameMode.Taiko)]
        [InlineData(GameMode.Mania, "*regulation gamemode TAIKO", GameMode.Taiko)]
        [InlineData(GameMode.Mania, "*regulation gamemode 1", GameMode.Taiko)]
        [InlineData(GameMode.Mania, "*regulation gamemode CatchTheBeat", GameMode.Catch)]
        [InlineData(GameMode.Mania, "*regulation gamemode fruits", GameMode.Catch)]
        [InlineData(GameMode.Mania, "*regulation gamemode catch", GameMode.Catch)]
        [InlineData(GameMode.Mania, "*regulation gamemode 2", GameMode.Catch)]
        [InlineData(GameMode.Osu, "*regulation gamemode OsuMania", GameMode.Mania)]
        [InlineData(GameMode.Osu, "*regulation gamemode mania", GameMode.Mania)]
        [InlineData(GameMode.Osu, "*regulation gamemode 3", GameMode.Mania)]
        [InlineData(GameMode.Osu, "*regulation gamemode", GameMode.Osu)]
        [InlineData(GameMode.Osu, "*regulation gamemode boss", GameMode.Osu)]
        [InlineData(GameMode.Osu, "*regulation gamemode  asfsdf ", GameMode.Osu)]
        [InlineData(GameMode.Osu, "*regulation gamemode 4", GameMode.Osu)]
        [InlineData(GameMode.Osu, "*regulation gamemode  * fdssdflk lsdf lksdfl3342r ", GameMode.Osu)]
        [InlineData(GameMode.Mania, "*no regulation gamemode", GameMode.Osu)]
        public void Game_mode(GameMode initial, string message, GameMode expected)
        {
            Assert.Equal(expected, Apply(new MapRegulation { Mode = initial }, message).Mode);
        }

        [Theory]
        [InlineData(false, "*regulation allow_convert", true)]
        [InlineData(true, "*regulation disallow_convert", false)]
        [InlineData(false, "*regulation allow_convert true", true)]
        [InlineData(true, "*regulation allow_convert false", false)]
        [InlineData(true, "*no regulation allow_convert", false)]
        public void Converts(bool initial, string message, bool expected)
        {
            Assert.Equal(expected, Apply(new MapRegulation { AllowConvert = initial }, message).AllowConvert);
        }

        [Fact]
        public void Several_settings_at_once()
        {
            var result = Apply(new MapRegulation { Mode = GameMode.Taiko }, "*regulation starmax=10 starmin=1 maxlen=100 lenmin = 20 gamemode= osu");
            Assert.Equal(GameMode.Osu, result.Mode);
            Assert.Equal(10, result.StarMax);
            Assert.Equal(1, result.StarMin);
            Assert.Equal(100, result.LengthMaxSeconds);
            Assert.Equal(20, result.LengthMinSeconds);
        }

        [Fact]
        public void A_bad_value_among_several_changes_nothing()
        {
            var before = new MapRegulation { StarMax = 5 };
            Assert.Equal(before, Apply(before, "*regulation starmax=7 gamemode=boss"));
        }

        [Fact]
        public void Other_commands_change_nothing()
        {
            var before = new MapRegulation();
            Assert.Null(RegulationCommand.Apply(before, "*regulationx", "star_max 5"));
            Assert.Null(RegulationCommand.Apply(before, "*no", "queue"));
        }
    }

    public class Description
    {
        [Fact]
        public void Disabled_shows_the_rules_in_brackets()
        {
            var regulation = new MapRegulation { Enabled = false, StarMax = 7, LengthMaxSeconds = 600 };
            Assert.Equal("Disabled (star rating <= 7.00, length <= 10:00, Mode: osu!)", regulation.Describe());
        }

        [Theory]
        [InlineData(0, 0, 0, 0, GameMode.Osu, true, "Mode: osu!")]
        [InlineData(0, 0, 0, 0, GameMode.Taiko, true, "Mode: osu!taiko (Converts allowed)")]
        [InlineData(0, 0, 0, 0, GameMode.Taiko, false, "Mode: osu!taiko (Converts disallowed)")]
        [InlineData(0, 0, 0, 0, GameMode.Catch, true, "Mode: osu!catch (Converts allowed)")]
        [InlineData(0, 0, 0, 0, GameMode.Mania, false, "Mode: osu!mania (Converts disallowed)")]
        [InlineData(0, 1, 0, 0, GameMode.Osu, true, "star rating <= 1.00, Mode: osu!")]
        [InlineData(1, 0, 0, 0, GameMode.Osu, true, "1.00 <= star rating, Mode: osu!")]
        [InlineData(1, 2, 0, 0, GameMode.Osu, true, "1.00 <= star rating <= 2.00, Mode: osu!")]
        [InlineData(0, 0, 0, 60, GameMode.Osu, true, "length <= 1:00, Mode: osu!")]
        [InlineData(0, 0, 90, 0, GameMode.Osu, true, "1:30 <= length, Mode: osu!")]
        [InlineData(0, 0, 30, 120, GameMode.Osu, true, "0:30 <= length <= 2:00, Mode: osu!")]
        [InlineData(1, 2, 30, 120, GameMode.Osu, true, "1.00 <= star rating <= 2.00, 0:30 <= length <= 2:00, Mode: osu!")]
        public void Describes_the_limits(double starMin, double starMax, int lengthMin, int lengthMax, GameMode mode, bool convert, string expected)
        {
            var regulation = new MapRegulation
            {
                StarMin = starMin, StarMax = starMax, LengthMinSeconds = lengthMin, LengthMaxSeconds = lengthMax, Mode = mode, AllowConvert = convert,
            };
            Assert.Equal(expected, regulation.Describe());
        }

        [Fact]
        public void Describes_a_map_from_the_template()
        {
            var text = new MapRegulation().DescribeMap(new BeatmapInfo(100, 200, "song", GameMode.Osu, 5.456, 125, "ranked"));
            Assert.StartsWith("[https://osu.ppy.sh/b/100 song] | Star Rating: 5.46 | Duration: 2:05 | Status: ranked", text);
            Assert.Contains("https://osu.direct/d/200", text);
        }

        [Theory]
        [InlineData(2.0, 90, 0.0)]    // exactly the minimum stars
        [InlineData(4.0, 90, 0.0)]    // exactly the maximum stars
        [InlineData(3.0, 60, 0.0)]    // exactly the minimum length
        [InlineData(3.0, 120, 0.0)]   // exactly the maximum length
        [InlineData(1.99, 90, 0.01)]
        [InlineData(4.01, 90, 0.01)]
        [InlineData(3.0, 59, 1 / 60.0)]
        [InlineData(3.0, 121, 1 / 60.0)]
        public void Limits_are_inclusive(double stars, int seconds, double expectedRate)
        {
            var regulation = new MapRegulation { StarMin = 2, StarMax = 4, LengthMinSeconds = 60, LengthMaxSeconds = 120 };
            var (rate, _) = regulation.Rate(LobbyHarness.Map(7, stars, seconds));
            Assert.Equal(expectedRate, rate, 6);
        }

        [Fact]
        public void Lists_every_reason_when_there_are_several()
        {
            var (rate, message) = new MapRegulation { StarMax = 5, LengthMaxSeconds = 60 }.Rate(LobbyHarness.Map(7, stars: 6.55, seconds: 90));
            Assert.True(rate > 0);
            Assert.Equal(
                "[https://osu.ppy.sh/b/7 title 7] (Star rating: 6.55, Length: 1:30) was rejected because of following reason:\n"
                + "- the beatmap star rating is higher than the allowed star rating.\n"
                + "- the beatmap length is longer than the allowed length.",
                message);
        }
    }

    public class Checks
    {
        private static LobbyHarness Create(MapRegulation regulation)
        {
            var lobby = new LobbyHarness(s => s with { Regulation = regulation, HostAfkCheckInterval = TimeSpan.Zero });
            lobby.AddPlayers("p1", "p2", "p3");
            return lobby;
        }

        private static void Pick(LobbyHarness lobby, int id, double stars = 5, int seconds = 120, GameMode mode = GameMode.Osu)
        {
            lobby.Beatmaps[id] = BeatmapLookup.Found(LobbyHarness.Map(id, stars, seconds, mode));
            lobby.PickMap(id);
        }

        // Phase 6, issue 05, room 122155463: after a restart the bot had no accepted map, an
        // off-mode pick could not be reverted, and "start when everyone is ready" started it.
        private static LobbyHarness WithRejectedFirstPick()
        {
            var lobby = Create(new MapRegulation { Mode = GameMode.Osu });
            lobby.Beatmaps[100] = new BeatmapLookup(BeatmapLookupOutcome.ModeMismatch);
            lobby.PickMap(100);
            Assert.True(lobby.Engine.CurrentMapRejected);
            lobby.Sent.Clear();
            return lobby;
        }

        [Fact]
        public void Everyone_ready_does_not_start_a_rejected_map()
        {
            var lobby = WithRejectedFirstPick();
            lobby.Feed(new AllPlayersReady());
            Assert.Equal(0, lobby.SentCount<StartMatch>());
            Assert.Contains(lobby.Said, s => s.Contains("does not fit the rules, so the match was not started", StringComparison.Ordinal));
        }

        [Fact]
        public void The_hosts_start_command_does_not_start_a_rejected_map()
        {
            var lobby = WithRejectedFirstPick();
            lobby.Chat(lobby.Engine.Room.Host!.Name, "!start");
            Assert.Equal(0, lobby.SentCount<StartMatch>());
        }

        [Fact]
        public void A_rejected_map_started_from_the_client_is_aborted()
        {
            var lobby = WithRejectedFirstPick();
            lobby.Feed(new MatchStarted());
            Assert.Equal(1, lobby.SentCount<AbortMatch>());
            Assert.Contains(lobby.Said, s => s.Contains("so the match was aborted", StringComparison.Ordinal));
            Assert.Equal(0, lobby.Engine.LastAcceptedMapId);
        }

        [Fact]
        public void The_owner_can_still_start_a_rejected_map()
        {
            var fromChat = WithRejectedFirstPick();
            fromChat.Chat("creator", "*start");
            Assert.Equal(1, fromChat.SentCount<StartMatch>());
            Assert.Equal(0, fromChat.SentCount<AbortMatch>());

            var fromDashboard = WithRejectedFirstPick();
            fromDashboard.Run(() => fromDashboard.Engine.ForceStart(0));
            Assert.Equal(1, fromDashboard.SentCount<StartMatch>());
            Assert.Equal(0, fromDashboard.SentCount<AbortMatch>());
        }

        [Fact]
        public void A_good_pick_after_a_rejected_one_starts_as_usual()
        {
            var lobby = WithRejectedFirstPick();
            Pick(lobby, 200);
            Assert.False(lobby.Engine.CurrentMapRejected);
            lobby.Feed(new AllPlayersReady());
            Assert.Equal(1, lobby.SentCount<StartMatch>());
        }

        [Fact]
        public void After_a_restart_the_rooms_map_is_checked_and_becomes_the_one_to_revert_to()
        {
            var lobby = Create(new MapRegulation { Mode = GameMode.Osu });
            lobby.Feed(new RoomStateReceived(new RoomState("room", 1, 50, "title 50", "HeadToHead", "Score", string.Empty, [])));
            Assert.Equal(50, Assert.Single(lobby.Sent.OfType<LookUpBeatmap>()).BeatmapId);
            Assert.Equal(50, lobby.Engine.LastAcceptedMapId);
            Assert.Empty(lobby.Said);

            lobby.Beatmaps[100] = new BeatmapLookup(BeatmapLookupOutcome.ModeMismatch);
            lobby.PickMap(100);
            Assert.Equal(50, lobby.Sent.OfType<SetBeatmap>().Last().BeatmapId);
            Assert.False(lobby.Engine.CurrentMapRejected);
        }

        [Fact]
        public void After_a_restart_a_rooms_map_that_breaks_the_rules_is_not_started()
        {
            var lobby = Create(new MapRegulation { Mode = GameMode.Osu });
            lobby.Beatmaps[50] = new BeatmapLookup(BeatmapLookupOutcome.ModeMismatch);
            lobby.Feed(new RoomStateReceived(new RoomState("room", 1, 50, "title 50", "HeadToHead", "Score", string.Empty, [])));
            Assert.Equal(0, lobby.Engine.LastAcceptedMapId);
            Assert.True(lobby.Engine.CurrentMapRejected);

            lobby.Feed(new AllPlayersReady());
            Assert.Equal(0, lobby.SentCount<StartMatch>());
        }

        [Fact]
        public void A_room_state_with_a_known_map_is_not_looked_up_again()
        {
            var lobby = Create(new MapRegulation());
            Pick(lobby, 50);
            var lookups = lobby.SentCount<LookUpBeatmap>();
            lobby.Feed(new RoomStateReceived(new RoomState("room", 1, 50, "title 50", "HeadToHead", "Score", string.Empty, [])));
            Assert.Equal(lookups, lobby.SentCount<LookUpBeatmap>());
        }

        [Fact]
        public void An_ordinary_map_passes_by_default()
        {
            var lobby = Create(new MapRegulation());
            Pick(lobby, 100);
            Assert.Equal(100, lobby.Engine.LastAcceptedMapId);
            var set = Assert.Single(lobby.Sent.OfType<SetBeatmap>());
            Assert.Equal(0, set.GameMode);
            Assert.StartsWith("[https://osu.ppy.sh/b/100 title 100] | Star Rating: 5.00", set.Note);
        }

        [Theory]
        [InlineData(5.0)]
        [InlineData(2.0)]
        [InlineData(5.004)]
        public void Stars_inside_the_limits_pass(double stars)
        {
            var lobby = Create(new MapRegulation { StarMin = 2, StarMax = 5 });
            Pick(lobby, 100, stars: stars);
            Assert.Equal(100, lobby.Engine.LastAcceptedMapId);
        }

        [Fact]
        public void No_upper_star_limit_means_any_high_star_rating_passes()
        {
            var lobby = Create(new MapRegulation { StarMin = 2 });
            Pick(lobby, 101, stars: 10);
            Assert.Equal(101, lobby.Engine.LastAcceptedMapId);
        }

        [Theory]
        [InlineData(5.01)]
        [InlineData(1.99)]
        public void Stars_outside_the_limits_are_rejected(double stars)
        {
            var lobby = Create(new MapRegulation { StarMin = 2, StarMax = 5 });
            Pick(lobby, 100, stars: stars);
            Assert.Equal(0, lobby.Engine.LastAcceptedMapId);
            Assert.Equal(1, lobby.Engine.MapViolations);
            Assert.Contains("Current regulation: 2.00 <= star rating <= 5.00, Mode: osu!", lobby.Said);
            Assert.Contains("Attention! Star rating will not be calculated correctly if a global mod is applied.", lobby.Said);
        }

        [Theory]
        [InlineData(100, true)]
        [InlineData(10, true)]
        [InlineData(101, false)]
        [InlineData(9, false)]
        public void Length_limits(int seconds, bool accepted)
        {
            var lobby = Create(new MapRegulation { LengthMinSeconds = 10, LengthMaxSeconds = 100 });
            Pick(lobby, 100, seconds: seconds);
            Assert.Equal(accepted ? 100 : 0, lobby.Engine.LastAcceptedMapId);
        }

        [Theory]
        [InlineData(GameMode.Taiko)]
        [InlineData(GameMode.Catch)]
        [InlineData(GameMode.Mania)]
        public void Maps_in_the_lobby_mode_pass_and_are_set_in_that_mode(GameMode mode)
        {
            var lobby = Create(new MapRegulation { Mode = mode, AllowConvert = false });
            Pick(lobby, 100, mode: mode);
            Assert.Equal(100, lobby.Engine.LastAcceptedMapId);
            Assert.Equal((int)mode, lobby.Sent.OfType<SetBeatmap>().Single().GameMode);
            Assert.Equal(mode, lobby.Sent.OfType<LookUpBeatmap>().Single().Mode);
            Assert.False(lobby.Sent.OfType<LookUpBeatmap>().Single().AllowConvert);
        }

        [Fact]
        public void A_map_the_catalog_finds_in_another_mode_is_rejected()
        {
            var lobby = Create(new MapRegulation { Mode = GameMode.Osu, AllowConvert = false });
            Pick(lobby, 100, mode: GameMode.Taiko);
            Assert.Equal(0, lobby.Engine.LastAcceptedMapId);
            Assert.Contains(lobby.Said, s => s.Contains("the gamemode is not osu!.", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(BeatmapLookupOutcome.NotFound, "[https://osu.ppy.sh/b/100 title 100] had already been removed from the website.")]
        [InlineData(BeatmapLookupOutcome.ModeMismatch, "[https://osu.ppy.sh/b/100 title 100] is not osu!taiko beatmap. Pick osu!taiko beatmap.")]
        [InlineData(BeatmapLookupOutcome.NotAvailable, "[https://osu.ppy.sh/b/100 title 100] is not available for download.")]
        public void Maps_the_catalog_cannot_serve_are_reverted_with_the_reason(BeatmapLookupOutcome outcome, string reason)
        {
            var lobby = Create(new MapRegulation { Mode = GameMode.Taiko });
            Pick(lobby, 50, mode: GameMode.Taiko);
            lobby.Beatmaps[100] = new BeatmapLookup(outcome);
            lobby.PickMap(100);

            var revert = lobby.Sent.OfType<SetBeatmap>().Last();
            Assert.Equal(new SetBeatmap(50, 1, reason), revert);
            Assert.Equal(1, lobby.Engine.MapViolations);
        }

        [Fact]
        public void A_rejected_map_goes_back_to_the_last_accepted_map()
        {
            var lobby = Create(new MapRegulation { StarMax = 5 });
            Pick(lobby, 50);
            Pick(lobby, 100, stars: 6);
            var revert = lobby.Sent.OfType<SetBeatmap>().Last();
            Assert.Equal(50, revert.BeatmapId);
            Assert.Equal("Current regulation: star rating <= 5.00, Mode: osu!", revert.Note);

            // The revert comes back from Bancho as a map change, which is not checked again.
            Assert.Equal(2, lobby.SentCount<LookUpBeatmap>());
            Assert.Equal(50, lobby.Engine.LastAcceptedMapId);
        }

        [Fact]
        public void With_no_accepted_map_yet_the_reason_is_only_said()
        {
            var lobby = Create(new MapRegulation { StarMax = 5 });
            Pick(lobby, 100, stars: 6);
            Assert.Equal(0, lobby.SentCount<SetBeatmap>());
            Assert.Contains("Current regulation: star rating <= 5.00, Mode: osu!", lobby.Said);
        }

        [Fact]
        public void A_failed_lookup_lets_the_map_through_silently()
        {
            var lobby = Create(new MapRegulation { StarMax = 5 });
            lobby.Beatmaps[100] = new BeatmapLookup(BeatmapLookupOutcome.Failed);
            lobby.PickMap(100);
            Assert.Equal(0, lobby.Engine.MapViolations);
            Assert.Equal(0, lobby.SentCount<SetBeatmap>());
            Assert.Empty(lobby.Said);
        }

        [Fact]
        public void Nothing_is_checked_while_disabled()
        {
            var lobby = Create(new MapRegulation { Enabled = false, StarMax = 3, Mode = GameMode.Osu });
            Pick(lobby, 100, stars: 15, mode: GameMode.Taiko);
            Assert.Equal(0, lobby.SentCount<LookUpBeatmap>());
            Assert.Equal(0, lobby.Engine.MapViolations);
        }

        [Fact]
        public void A_late_answer_for_a_map_already_replaced_is_ignored()
        {
            var lobby = Create(new MapRegulation { StarMax = 5 });
            lobby.AnswerLookupsAtOnce = false;
            lobby.Beatmaps[100] = BeatmapLookup.Found(LobbyHarness.Map(100, stars: 9));
            lobby.PickMap(100);
            lobby.PickMap(101);
            lobby.AnswerLookups();

            Assert.Equal(0, lobby.Engine.MapViolations);
            Assert.Equal(101, lobby.Engine.LastAcceptedMapId);
        }

        [Fact]
        public void A_match_started_before_the_answer_counts_the_map_as_accepted()
        {
            var lobby = Create(new MapRegulation { StarMax = 5 });
            lobby.AnswerLookupsAtOnce = false;
            lobby.PickMap(100);
            lobby.StartMatch();
            lobby.AnswerLookups();
            Assert.Equal(100, lobby.Engine.LastAcceptedMapId);
            Assert.Equal(0, lobby.Engine.MapViolations);
        }

        [Fact]
        public void Three_violations_skip_the_host_and_a_new_host_starts_clean()
        {
            var lobby = Create(new MapRegulation { StarMax = 5 });
            lobby.AssertHost("p1");
            for (var i = 1; i <= 2; i++)
            {
                Pick(lobby, 100, stars: 6.55);
                Assert.Equal(i, lobby.Engine.MapViolations);
                lobby.AssertHost("p1");
            }

            Pick(lobby, 100, stars: 6.55);
            lobby.AssertHost("p2");
            Assert.Contains("The number of violations has reached 3. Skipped player p1", lobby.Said);
            Assert.Equal(0, lobby.Engine.MapViolations);
        }

        [Fact]
        public void Zero_allowed_violations_never_skips()
        {
            var lobby = Create(new MapRegulation { StarMax = 5, ViolationsAllowed = 0 });
            for (var i = 0; i < 5; i++)
            {
                Pick(lobby, 100, stars: 6);
            }

            lobby.AssertHost("p1");
            Assert.Equal(5, lobby.Engine.MapViolations);
        }

        [Fact]
        public void Anyone_can_ask_for_the_regulation_once_per_10_seconds()
        {
            var lobby = Create(new MapRegulation { StarMax = 5 });
            lobby.Chat("p2", "!r");
            lobby.Chat("p3", "!regulation");
            Assert.Equal(["star rating <= 5.00, Mode: osu!"], lobby.Said);

            lobby.Time.Advance(TimeSpan.FromSeconds(10));
            lobby.Chat("p3", "!regulation");
            Assert.Equal(2, lobby.Said.Count());
        }

        [Fact]
        public void The_owner_changes_the_regulation_and_players_hear_it()
        {
            var lobby = Create(new MapRegulation());
            lobby.Chat(LobbyHarness.Owner, "*regulation star_max 6");
            Assert.Equal(6, lobby.Engine.Settings.Regulation.StarMax);
            Assert.Contains("New regulation: star rating <= 6.00, Mode: osu!", lobby.Said);

            lobby.Chat(LobbyHarness.Owner, "*mapchecker_disable");
            Assert.False(lobby.Engine.Settings.Regulation.Enabled);
            Assert.Contains("Map Checker plugin disabled.", lobby.Said);

            lobby.Chat("p1", "*regulation star_max 9");
            Assert.Equal(6, lobby.Engine.Settings.Regulation.StarMax);
        }

        [Fact]
        public void Auto_start_waits_for_an_accepted_map()
        {
            var lobby = new LobbyHarness(s => s with
            {
                Regulation = new MapRegulation { StarMax = 5 },
                AutoStartEnabled = true,
                HostAfkCheckInterval = TimeSpan.Zero,
            });
            lobby.AddPlayers("p1", "p2");
            Pick(lobby, 100, stars: 6);
            Assert.False(lobby.Engine.IsStartTimerRunning);

            Pick(lobby, 101, stars: 4);
            Assert.True(lobby.Engine.IsStartTimerRunning);
        }
    }
}
