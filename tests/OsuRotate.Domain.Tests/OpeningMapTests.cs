using OsuRotate.Domain;

namespace OsuRotate.Domain.Tests;

// Map pick, issue 09: a room the bot makes opens on a map that fits the rules, so the first host's
// map has something to be reverted to.
public class OpeningMapTests
{
    private static readonly MapRegulation Rules = new() { StarMin = 4, StarMax = 6 };
    private static readonly BeatmapInfo Opening = LobbyHarness.Map(700, stars: 5);

    private static LobbyHarness Create(MapRegulation? regulation = null, bool pick = false, bool autoStart = false)
    {
        var lobby = new LobbyHarness(s => s with
        {
            Regulation = regulation ?? Rules,
            MapPick = new MapPickSettings { Enabled = pick, OldestRankedYear = 2014 },
            AutoStartEnabled = autoStart,
            HostAfkCheckInterval = TimeSpan.Zero,
        })
        {
            OpeningMap = Opening,
        };
        return lobby;
    }

    private static void CreateRoom(LobbyHarness lobby) => lobby.Feed(new RoomCreated(1, "room"));

    private static int SetCount(LobbyHarness lobby, int beatmapId) => lobby.Sent.OfType<SetBeatmap>().Count(s => s.BeatmapId == beatmapId);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Making_a_room_asks_for_a_map_that_fits_the_rules(bool pick)
    {
        var lobby = Create(regulation: Rules with { Mode = GameMode.Taiko, AllowConvert = false, LengthMaxSeconds = 300 }, pick: pick);
        lobby.OpeningMap = null;

        CreateRoom(lobby);

        Assert.Equal(
            new ChooseOpeningMap(new MapPickQuery(GameMode.Taiko, 4, 6, 0, 300, 2014), AllowConvert: false, FromPool: pick),
            Assert.Single(lobby.Sent.OfType<ChooseOpeningMap>()));
    }

    private static RoomState StateWithMap(int beatmapId) => new("room", 1, beatmapId, beatmapId == 0 ? string.Empty : "map", "HeadToHead", "Score", "Freemod", []);

    [Fact]
    public void Rejoining_a_room_keeps_its_map()
    {
        var lobby = Create();

        lobby.Feed(new RoomJoined(1));
        lobby.Feed(new RoomStateReceived(StateWithMap(853167)));

        Assert.Equal(0, lobby.SentCount<ChooseOpeningMap>());
        Assert.Equal(0, SetCount(lobby, 700));
    }

    [Fact]
    public void A_rejoined_room_with_no_map_gets_an_opening_map_once()
    {
        var lobby = Create();

        lobby.Feed(new RoomJoined(1));
        lobby.Feed(new RoomStateReceived(StateWithMap(0)));
        lobby.Feed(new RoomStateReceived(StateWithMap(0)));

        Assert.Equal(1, lobby.SentCount<ChooseOpeningMap>());
        Assert.Equal(1, SetCount(lobby, 700));
        Assert.Equal(700, lobby.Engine.LastAcceptedMapId);
    }

    [Fact]
    public void A_made_room_is_not_asked_about_again_when_its_state_shows_no_map()
    {
        var lobby = Create();
        lobby.OpeningMap = null;

        CreateRoom(lobby);
        lobby.Feed(new RoomStateReceived(StateWithMap(0)));

        Assert.Equal(1, lobby.SentCount<ChooseOpeningMap>());
    }

    [Fact]
    public void A_room_whose_map_changed_before_its_state_came_is_not_asked_about()
    {
        var lobby = Create();
        lobby.Feed(new RoomJoined(1));
        lobby.PickMap(800);

        lobby.Feed(new RoomStateReceived(StateWithMap(0)));

        Assert.Equal(0, lobby.SentCount<ChooseOpeningMap>());
    }

    [Fact]
    public void The_opening_map_is_set_as_an_accepted_map()
    {
        var lobby = Create();

        CreateRoom(lobby);

        Assert.Contains(new SetBeatmap(700, (int)GameMode.Osu, Rules.DescribeMap(Opening)), lobby.Sent);
        Assert.Equal(0, lobby.SentCount<LookUpBeatmap>());
        Assert.Equal(700, lobby.Engine.LastAcceptedMapId);
    }

    [Fact]
    public void Without_an_answer_the_room_is_left_as_it_was()
    {
        var lobby = Create();
        lobby.OpeningMap = null;

        CreateRoom(lobby);

        Assert.Equal(0, lobby.SentCount<SetBeatmap>());
        Assert.Equal(0, lobby.Engine.LastAcceptedMapId);
    }

    [Fact]
    public void A_hosts_map_outside_the_rules_goes_back_to_the_opening_map()
    {
        var lobby = Create();
        lobby.Beatmaps[800] = BeatmapLookup.Found(LobbyHarness.Map(800, stars: 7));
        CreateRoom(lobby);
        lobby.AddPlayers("p1", "p2");

        lobby.PickMap(800);

        Assert.Equal(2, SetCount(lobby, 700));
        Assert.False(lobby.Engine.CurrentMapRejected);
        Assert.Equal(1, lobby.Engine.MapViolations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_opening_map_waits_for_a_host_instead_of_starting(bool regulation)
    {
        var lobby = Create(regulation: Rules with { Enabled = regulation }, autoStart: true);
        lobby.AnswerOpeningMapAtOnce = false;
        CreateRoom(lobby);
        lobby.AddPlayers("p1", "p2", "p3");

        lobby.AnswerOpeningMap();

        Assert.Equal(1, SetCount(lobby, 700));
        Assert.DoesNotContain(lobby.Said, s => s.StartsWith("Queued the match", StringComparison.Ordinal));
        lobby.Wait(TimeSpan.FromMinutes(5));
        Assert.Equal(0, lobby.SentCount<StartMatch>());
    }

    [Fact]
    public void A_hosts_own_map_afterwards_starts_auto_start()
    {
        var lobby = Create(regulation: Rules with { Enabled = false }, autoStart: true);
        lobby.AnswerOpeningMapAtOnce = false;
        CreateRoom(lobby);
        lobby.AddPlayers("p1", "p2", "p3");
        lobby.AnswerOpeningMap();

        lobby.PickMap(800);

        Assert.Contains(lobby.Said, s => s.StartsWith("Queued the match", StringComparison.Ordinal));
    }

    [Fact]
    public void A_hosts_accepted_map_is_not_replaced()
    {
        var lobby = Create();
        lobby.AnswerOpeningMapAtOnce = false;
        CreateRoom(lobby);
        lobby.AddPlayers("p1", "p2");
        lobby.PickMap(800);

        lobby.AnswerOpeningMap();

        Assert.Equal(0, SetCount(lobby, 700));
        Assert.Equal(800, lobby.Engine.LastAcceptedMapId);
    }

    [Fact]
    public void A_hosts_map_with_the_regulation_off_is_not_replaced()
    {
        var lobby = Create(regulation: Rules with { Enabled = false });
        lobby.AnswerOpeningMapAtOnce = false;
        CreateRoom(lobby);
        lobby.AddPlayers("p1", "p2");
        lobby.PickMap(800);

        lobby.AnswerOpeningMap();

        Assert.Equal(0, SetCount(lobby, 700));
    }

    [Fact]
    public void A_rejected_map_with_nothing_to_go_back_to_is_replaced()
    {
        var lobby = Create();
        lobby.AnswerOpeningMapAtOnce = false;
        lobby.Beatmaps[800] = BeatmapLookup.Found(LobbyHarness.Map(800, stars: 7));
        CreateRoom(lobby);
        lobby.AddPlayers("p1", "p2");
        lobby.PickMap(800);
        Assert.True(lobby.Engine.CurrentMapRejected);

        lobby.AnswerOpeningMap();

        Assert.Equal(1, SetCount(lobby, 700));
        Assert.False(lobby.Engine.CurrentMapRejected);
        Assert.Equal(700, lobby.Engine.LastAcceptedMapId);
    }

    [Theory]
    [InlineData(7, 700)]
    [InlineData(5, 800)]
    public void While_a_hosts_map_is_checked_the_opening_map_is_only_kept_to_go_back_to(double hostStars, int accepted)
    {
        var lobby = Create();
        lobby.AnswerOpeningMapAtOnce = false;
        lobby.AnswerLookupsAtOnce = false;
        lobby.Beatmaps[800] = BeatmapLookup.Found(LobbyHarness.Map(800, stars: hostStars));
        CreateRoom(lobby);
        lobby.AddPlayers("p1", "p2");
        lobby.PickMap(800);

        lobby.AnswerOpeningMap();
        Assert.Equal(0, SetCount(lobby, 700));

        lobby.AnswerLookups();
        Assert.Equal(accepted, lobby.Engine.LastAcceptedMapId);
        Assert.Equal(accepted == 700 ? 1 : 0, SetCount(lobby, 700));
    }

    [Fact]
    public void A_map_that_no_longer_fits_the_rules_is_dropped()
    {
        var lobby = Create();
        lobby.AnswerOpeningMapAtOnce = false;
        CreateRoom(lobby);
        lobby.Engine.UpdateSettings(lobby.Engine.Settings with { Regulation = Rules with { StarMax = 4.5 } });

        lobby.AnswerOpeningMap();

        Assert.Equal(0, lobby.SentCount<SetBeatmap>());
    }

    [Fact]
    public void An_answer_during_a_match_is_dropped()
    {
        var lobby = Create();
        lobby.AnswerOpeningMapAtOnce = false;
        CreateRoom(lobby);
        lobby.AddPlayers("p1", "p2");
        lobby.StartMatch();

        lobby.AnswerOpeningMap();

        Assert.Equal(0, SetCount(lobby, 700));
    }
}
