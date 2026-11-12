using OsuRotate.Domain;

namespace OsuRotate.Domain.Tests;

// !info, !help, !version and the info announcement from osu-ahr's Lobby, !update from MapRecaster
// (its 3 tests are the Recast class) and !mirror and !rank from MiscLoader, which had no tests.
public class InfoCommandTests
{
    private static LobbyHarness Create(Func<LobbySettings, LobbySettings>? configure = null)
    {
        var lobby = new LobbyHarness(s =>
        {
            s = s with { HostAfkCheckInterval = TimeSpan.Zero, BotVersion = "1.2.3" };
            return configure?.Invoke(s) ?? s;
        });
        lobby.AddPlayers("p1", "p2", "p3");
        lobby.Sent.Clear();
        return lobby;
    }

    public class Info
    {
        [Theory]
        [InlineData("!info")]
        [InlineData("!help")]
        [InlineData("!INFO")]
        public void Says_the_info_message_with_the_version(string message)
        {
            var lobby = Create(s => s with { InfoMessage = "bot ${version} here" });
            lobby.Chat("p1", message);
            Assert.Equal(["bot 1.2.3 here"], lobby.Said);
        }

        [Fact]
        public void The_default_message_names_the_version_and_the_commands()
        {
            var lobby = Create();
            lobby.Chat("p1", "!info");
            var text = Assert.Single(lobby.Said);
            Assert.StartsWith("- osu-rotate 1.2.3 -\n", text);
            Assert.Contains("!skip", text);
        }

        [Fact]
        public void Has_a_cooldown_shared_by_info_and_help()
        {
            var lobby = Create();
            lobby.Chat("p1", "!info");
            lobby.Chat("p2", "!help");
            lobby.Time.Advance(TimeSpan.FromSeconds(59));
            lobby.Chat("p2", "!info");
            Assert.Single(lobby.Said);

            lobby.Time.Advance(TimeSpan.FromSeconds(1));
            lobby.Chat("p2", "!info");
            Assert.Equal(2, lobby.Said.Count());
        }

        [Fact]
        public void Players_outside_the_lobby_are_not_answered()
        {
            var lobby = Create();
            lobby.Chat("stranger", "!info");
            Assert.Empty(lobby.Sent);
        }

        [Theory]
        [InlineData("!info")]
        [InlineData("!help")]
        [InlineData("  !Help ")]
        public void A_private_message_is_answered_in_private(string message)
        {
            var lobby = Create(s => s with { InfoMessage = "info ${version}" });
            lobby.Feed(new PrivateMessageReceived("p1", message));
            Assert.Equal([new Whisper("p1", "info 1.2.3")], lobby.Sent);
        }

        [Fact]
        public void Private_answers_have_a_cooldown_per_player()
        {
            var lobby = Create();
            lobby.Feed(new PrivateMessageReceived("p1", "!info"));
            lobby.Feed(new PrivateMessageReceived("p1", "!help"));
            lobby.Feed(new PrivateMessageReceived("p2", "!info"));
            Assert.Equal(["p1", "p2"], lobby.Sent.OfType<Whisper>().Select(w => w.Username));

            lobby.Time.Advance(TimeSpan.FromSeconds(60));
            lobby.Feed(new PrivateMessageReceived("p1", "!info"));
            Assert.Equal(3, lobby.SentCount<Whisper>());
        }

        [Theory]
        [InlineData("stranger", "!info")]
        [InlineData("p1", "!infos")]
        [InlineData("p1", "hello")]
        [InlineData("p1", "!version")]
        public void Other_private_messages_get_no_answer(string from, string message)
        {
            var lobby = Create();
            lobby.Feed(new PrivateMessageReceived(from, message));
            Assert.Empty(lobby.Sent);
        }

        [Theory]
        [InlineData("!version")]
        [InlineData("!v")]
        public void Says_the_version(string message)
        {
            var lobby = Create();
            lobby.Chat("p1", message);
            lobby.Chat("p2", message);
            Assert.Equal(["osu-rotate v. 1.2.3"], lobby.Said);

            lobby.Time.Advance(TimeSpan.FromSeconds(60));
            lobby.Chat("p2", message);
            Assert.Equal(2, lobby.Said.Count());
        }

        [Fact]
        public void Info_and_version_have_separate_cooldowns()
        {
            var lobby = Create(s => s with { InfoMessage = "hi" });
            lobby.Chat("p1", "!info");
            lobby.Chat("p1", "!version");
            Assert.Equal(["hi", "osu-rotate v. 1.2.3"], lobby.Said);
        }

        [Fact]
        public void The_announcement_repeats_once_the_room_is_entered()
        {
            var lobby = Create(s => s with { InfoMessage = "hi", InfoAnnouncementInterval = TimeSpan.FromMinutes(5) });
            lobby.Wait(TimeSpan.FromMinutes(10));
            Assert.Empty(lobby.Said);

            lobby.Feed(new RoomJoined(1));
            lobby.Wait(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
            Assert.Empty(lobby.Said);
            lobby.Wait(TimeSpan.FromSeconds(1));
            Assert.Equal(["hi"], lobby.Said);
            lobby.Wait(TimeSpan.FromMinutes(5));
            Assert.Equal(["hi", "hi"], lobby.Said);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(3)]
        public void Short_intervals_never_announce(int minutes)
        {
            var lobby = Create(s => s with { InfoAnnouncementInterval = TimeSpan.FromMinutes(minutes) });
            lobby.Feed(new RoomCreated(1, "room"));
            lobby.Wait(TimeSpan.FromMinutes(30));
            Assert.Empty(lobby.Said);
        }

        [Fact]
        public void Turning_the_announcement_off_stops_it_on_the_next_entry()
        {
            var lobby = Create(s => s with { InfoAnnouncementInterval = TimeSpan.FromMinutes(5) });
            lobby.Feed(new RoomJoined(1));
            lobby.Engine.UpdateSettings(lobby.Engine.Settings with { InfoAnnouncementInterval = TimeSpan.Zero });
            lobby.Feed(new RoomJoined(1));
            lobby.Wait(TimeSpan.FromMinutes(30));
            Assert.Empty(lobby.Said);
        }
    }

    public class Recast
    {
        private static LobbyHarness CreateWithMap()
        {
            var lobby = Create(s => s with { Regulation = s.Regulation with { Enabled = false } });
            lobby.PickMap(100);
            lobby.Sent.Clear();
            return lobby;
        }

        [Fact]
        public void Update_sets_the_current_map_again()
        {
            var lobby = CreateWithMap();
            lobby.Chat("p1", "!update");
            Assert.Equal([new SetBeatmap(100)], lobby.Sent);
        }

        [Fact]
        public void Only_once_per_map()
        {
            var lobby = CreateWithMap();
            lobby.Chat("p1", "!update");
            lobby.Chat("p2", "!update");
            Assert.Equal(1, lobby.SentCount<SetBeatmap>());
        }

        [Fact]
        public void Again_every_time_the_host_picks_a_map()
        {
            var lobby = CreateWithMap();
            lobby.Chat("p1", "!update");
            lobby.PickMap(101);
            lobby.Chat("p2", "!update");
            lobby.PickMap(102);
            lobby.Chat("p2", "!update");
            Assert.Equal([100, 101, 102], lobby.Sent.OfType<SetBeatmap>().Select(s => s.BeatmapId));
        }

        [Fact]
        public void The_bots_own_map_change_does_not_allow_another()
        {
            var lobby = CreateWithMap();
            lobby.Chat("p1", "!update");
            lobby.Feed(new BeatmapChanged(100, "title 100", true));
            lobby.Chat("p1", "!update");
            Assert.Equal(1, lobby.SentCount<SetBeatmap>());
        }

        [Fact]
        public void Nothing_to_update_before_a_map_is_picked()
        {
            var lobby = Create();
            lobby.Chat("p1", "!update");
            Assert.Empty(lobby.Sent);
        }
    }

    public class Mirror
    {
        private const string Links = "Alternative download link for beatmap title 100: "
            + "[https://beatconnect.io/b/1000 BeatConnect.io] | [https://osu.direct/d/1000 osu.direct]";

        [Fact]
        public void Uses_the_map_the_checker_already_looked_up()
        {
            var lobby = Create();
            lobby.PickMap(100);
            lobby.Sent.Clear();
            lobby.Chat("p1", "!mirror");
            Assert.Equal([new Say(Links)], lobby.Sent);
        }

        [Fact]
        public void Looks_the_map_up_when_the_checker_is_off()
        {
            var lobby = Create(s => s with { Regulation = s.Regulation with { Enabled = false, Mode = GameMode.Taiko } });
            lobby.PickMap(100);
            Assert.DoesNotContain(lobby.Sent, c => c is LookUpBeatmap);

            lobby.Chat("p1", "!mirror");
            Assert.Contains(new LookUpBeatmap(100, GameMode.Taiko, true), lobby.Sent);
            Assert.Equal([Links], lobby.Said);

            // The answer is kept for the next request.
            lobby.Time.Advance(TimeSpan.FromSeconds(5));
            lobby.Chat("p2", "!mirror");
            Assert.Equal(1, lobby.SentCount<LookUpBeatmap>());
            Assert.Equal(2, lobby.Said.Count());
        }

        [Fact]
        public void Waits_for_the_checker_instead_of_asking_twice()
        {
            var lobby = Create();
            lobby.AnswerLookupsAtOnce = false;
            lobby.PickMap(100);
            lobby.Chat("p1", "!mirror");
            lobby.Time.Advance(TimeSpan.FromSeconds(5));
            lobby.Chat("p1", "!mirror");
            Assert.Equal(1, lobby.SentCount<LookUpBeatmap>());
            Assert.Empty(lobby.Said);

            lobby.AnswerLookups();
            Assert.Contains(Links, lobby.Said);
        }

        [Fact]
        public void A_pending_request_is_asked_only_once()
        {
            var lobby = Create(s => s with { Regulation = s.Regulation with { Enabled = false } });
            lobby.AnswerLookupsAtOnce = false;
            lobby.PickMap(100);
            lobby.Chat("p1", "!mirror");
            lobby.Time.Advance(TimeSpan.FromSeconds(5));
            lobby.Chat("p1", "!mirror");
            Assert.Equal(1, lobby.SentCount<LookUpBeatmap>());

            lobby.AnswerLookups();
            Assert.Equal([Links], lobby.Said);
        }

        [Theory]
        [InlineData(BeatmapLookupOutcome.NotFound)]
        [InlineData(BeatmapLookupOutcome.ModeMismatch)]
        [InlineData(BeatmapLookupOutcome.NotAvailable)]
        public void A_map_without_a_download_says_so(BeatmapLookupOutcome outcome)
        {
            var lobby = Create(s => s with { Regulation = s.Regulation with { Enabled = false } });
            lobby.Beatmaps[100] = new BeatmapLookup(outcome, null);
            lobby.PickMap(100);
            lobby.Chat("p1", "!mirror");
            Assert.Equal(["The current beatmap doesn't have a mirror."], lobby.Said);
        }

        [Fact]
        public void A_failed_lookup_says_nothing()
        {
            var lobby = Create(s => s with { Regulation = s.Regulation with { Enabled = false } });
            lobby.Beatmaps[100] = new BeatmapLookup(BeatmapLookupOutcome.Failed, null);
            lobby.PickMap(100);
            lobby.Chat("p1", "!mirror");
            Assert.Empty(lobby.Said);
        }

        [Fact]
        public void An_answer_for_an_older_map_is_not_used()
        {
            var lobby = Create(s => s with { Regulation = s.Regulation with { Enabled = false } });
            lobby.AnswerLookupsAtOnce = false;
            lobby.PickMap(100);
            lobby.Chat("p1", "!mirror");
            lobby.PickMap(101);
            lobby.AnswerLookups();
            Assert.Empty(lobby.Said);

            lobby.Time.Advance(TimeSpan.FromSeconds(5));
            lobby.Chat("p1", "!mirror");
            Assert.Equal([101], lobby.PendingLookups.Select(l => l.BeatmapId));
        }

        [Fact]
        public void A_request_left_unanswered_on_an_old_map_is_asked_again_when_that_map_comes_back()
        {
            var lobby = Create(s => s with { Regulation = s.Regulation with { Enabled = false } });
            lobby.AnswerLookupsAtOnce = false;
            lobby.PickMap(100);
            lobby.Chat("p1", "!mirror");
            lobby.PickMap(101);
            lobby.Beatmaps[100] = new BeatmapLookup(BeatmapLookupOutcome.Failed, null);
            lobby.AnswerLookups();
            lobby.PickMap(100);

            lobby.Time.Advance(TimeSpan.FromSeconds(5));
            lobby.AnswerLookupsAtOnce = true;
            lobby.Beatmaps.Remove(100);
            lobby.Chat("p1", "!mirror");
            Assert.Equal([Links], lobby.Said);
        }

        [Fact]
        public void A_map_without_a_download_never_shows_the_last_maps_links()
        {
            var lobby = Create(s => s with { Regulation = s.Regulation with { Enabled = false } });
            lobby.PickMap(100);
            lobby.Chat("p1", "!mirror");
            lobby.Beatmaps[101] = new BeatmapLookup(BeatmapLookupOutcome.NotFound, null);
            lobby.PickMap(101);
            lobby.Time.Advance(TimeSpan.FromSeconds(5));
            lobby.Chat("p1", "!mirror");
            Assert.Equal([Links, "The current beatmap doesn't have a mirror."], lobby.Said);
        }

        [Fact]
        public void A_new_map_forgets_the_old_links()
        {
            var lobby = Create();
            lobby.PickMap(100);
            lobby.PickMap(101);
            lobby.Sent.Clear();
            lobby.Chat("p1", "!mirror");
            Assert.Contains("https://beatconnect.io/b/1010", Assert.Single(lobby.Said));
        }

        [Fact]
        public void Has_a_cooldown()
        {
            var lobby = Create();
            lobby.PickMap(100);
            lobby.Chat("p1", "!mirror");
            lobby.Time.Advance(TimeSpan.FromSeconds(4));
            lobby.Chat("p2", "!mirror");
            Assert.Single(lobby.Said);

            lobby.Time.Advance(TimeSpan.FromSeconds(1));
            lobby.Chat("p2", "!mirror");
            Assert.Equal(2, lobby.Said.Count());
        }

        [Fact]
        public void Nothing_before_a_map_is_picked()
        {
            var lobby = Create();
            lobby.Chat("p1", "!mirror");
            Assert.Empty(lobby.Sent);
        }
    }

    public class Rank
    {
        [Fact]
        public void Asks_banchobot_for_the_players_stats_in_the_lobby()
        {
            var lobby = Create();
            lobby.Chat("p1", "!rank");
            Assert.Equal([new RequestStat("p1", ViaPrivateMessage: false)], lobby.Sent);
        }

        [Fact]
        public void Has_a_cooldown()
        {
            var lobby = Create();
            lobby.Chat("p1", "!rank");
            lobby.Time.Advance(TimeSpan.FromSeconds(9));
            lobby.Chat("p2", "!rank");
            Assert.Equal(1, lobby.SentCount<RequestStat>());

            lobby.Time.Advance(TimeSpan.FromSeconds(1));
            lobby.Chat("p2", "!rank");
            Assert.Equal(["p1", "p2"], lobby.Sent.OfType<RequestStat>().Select(r => r.Username));
        }

        [Fact]
        public void The_owner_can_ask_from_outside_the_lobby()
        {
            var lobby = Create();
            lobby.Chat(LobbyHarness.Owner, "!rank");
            Assert.Equal([new RequestStat(LobbyHarness.Owner, ViaPrivateMessage: false)], lobby.Sent);
        }

        [Fact]
        public void Strangers_are_ignored()
        {
            var lobby = Create();
            lobby.Chat("stranger", "!rank");
            Assert.Empty(lobby.Sent);
        }
    }
}
