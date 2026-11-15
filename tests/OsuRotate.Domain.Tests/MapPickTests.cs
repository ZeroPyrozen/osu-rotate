using OsuRotate.Domain;

namespace OsuRotate.Domain.Tests;

// Map pick, issue 04: the !pick command. The host's pick sets the map; anyone else's is a suggestion.
public class MapPickTests
{
    private static readonly MapRegulation Rules = new() { StarMin = 4, StarMax = 6 };

    private static LobbyHarness Create(bool enabled = true, MapRegulation? regulation = null, int oldestYear = 2012)
    {
        var lobby = new LobbyHarness(s => s with
        {
            Regulation = regulation ?? Rules,
            MapPick = new MapPickSettings { Enabled = enabled, OldestRankedYear = oldestYear },
            HostAfkCheckInterval = TimeSpan.Zero,
        });
        lobby.AddPlayers("p1", "p2", "p3");
        lobby.AssertHost("p1");
        lobby.Sent.Clear();
        return lobby;
    }

    private static MapRegulation RegulationOf(LobbyHarness lobby) => lobby.Engine.Settings.Regulation;

    [Fact]
    public void It_is_off_by_default()
    {
        Assert.False(new LobbySettings { OwnerName = "x" }.MapPick.Enabled);
        var lobby = Create(enabled: false);

        lobby.Chat("p1", "!pick");

        Assert.Empty(lobby.Sent);
    }

    [Fact]
    public void The_hosts_pick_sets_the_map_without_checking_it_again()
    {
        var lobby = Create();
        var map = LobbyHarness.Map(500);

        lobby.Chat("p1", "!pick");

        var pick = Assert.Single(lobby.Sent.OfType<PickBeatmap>());
        Assert.Equal(new PickBeatmap("p1", AsHost: true, new MapPickQuery(GameMode.Osu, 4, 6, 0, 0, 2012)), pick);
        Assert.Contains(new SetBeatmap(500, (int)GameMode.Osu, RegulationOf(lobby).DescribeMap(map)), lobby.Sent);
        Assert.Equal(0, lobby.SentCount<LookUpBeatmap>());
        Assert.Equal(500, lobby.Engine.LastAcceptedMapId);
        Assert.False(lobby.Engine.CurrentMapRejected);
    }

    [Fact]
    public void Suggest_works_too()
    {
        var lobby = Create();

        lobby.Chat("p1", "!suggest");

        Assert.Single(lobby.Sent.OfType<PickBeatmap>());
    }

    [Fact]
    public void Anyone_elses_pick_is_only_a_suggestion()
    {
        var lobby = Create();

        lobby.Chat("p2", "!pick");

        Assert.Equal(0, lobby.SentCount<SetBeatmap>());
        Assert.Contains($"Suggestion from p2: {RegulationOf(lobby).DescribeMap(LobbyHarness.Map(500))}", lobby.Said);
    }

    [Fact]
    public void The_stored_limits_are_used_while_the_regulation_is_off()
    {
        var lobby = Create(regulation: Rules with { Enabled = false, LengthMaxSeconds = 300, Mode = GameMode.Taiko }, oldestYear: 2016);

        lobby.Chat("p2", "!pick");

        Assert.Equal(new MapPickQuery(GameMode.Taiko, 4, 6, 0, 300, 2016), Assert.Single(lobby.Sent.OfType<PickBeatmap>()).Query);
    }

    [Fact]
    public void The_hosts_pick_with_the_regulation_off_starts_one_auto_start_wait()
    {
        var lobby = new LobbyHarness(s => s with
        {
            Regulation = Rules with { Enabled = false },
            MapPick = new MapPickSettings { Enabled = true },
            AutoStartEnabled = true,
            HostAfkCheckInterval = TimeSpan.Zero,
        });
        lobby.AddPlayers("p1", "p2");
        lobby.Sent.Clear();

        lobby.Chat("p1", "!pick");

        Assert.Single(lobby.Said, s => s.StartsWith("Queued the match to start", StringComparison.Ordinal));
    }

    [Fact]
    public void Picks_during_a_match_are_ignored()
    {
        var lobby = Create();
        lobby.StartMatch();
        lobby.Sent.Clear();

        lobby.Chat("p1", "!pick");
        lobby.Chat("p2", "!pick");

        Assert.Empty(lobby.Sent);
    }

    [Fact]
    public void Each_player_waits_30_seconds_and_the_room_10_without_a_reply()
    {
        var lobby = Create();

        lobby.Chat("p2", "!pick");                       // 0 s: picked
        lobby.Wait(TimeSpan.FromSeconds(5));
        lobby.Chat("p3", "!pick");                       // 5 s: the room waits
        lobby.Wait(TimeSpan.FromSeconds(6));
        lobby.Chat("p3", "!pick");                       // 11 s: picked, the ignored one didn't count
        lobby.Wait(TimeSpan.FromSeconds(11));
        lobby.Chat("p2", "!pick");                       // 22 s: p2 waits
        lobby.Wait(TimeSpan.FromSeconds(9));
        lobby.Chat("p2", "!pick");                       // 31 s: picked

        Assert.Equal(["p2", "p3", "p2"], lobby.Sent.OfType<PickBeatmap>().Select(p => p.Requester));
        Assert.Equal(3, lobby.Said.Count(s => s.StartsWith("Suggestion from", StringComparison.Ordinal)));
        Assert.Equal(3, lobby.Said.Count());
    }

    [Fact]
    public void A_hosts_pick_answered_after_the_host_changed_is_dropped()
    {
        var lobby = Create();
        lobby.AnswerPicksAtOnce = false;
        lobby.Chat("p1", "!pick");
        lobby.ChangeHostManually("p2");
        lobby.Sent.Clear();

        lobby.AnswerPicks();

        Assert.Empty(lobby.Sent);
    }

    [Fact]
    public void A_pick_answered_after_a_match_started_is_dropped()
    {
        var lobby = Create();
        lobby.AnswerPicksAtOnce = false;
        lobby.Chat("p1", "!pick");
        lobby.Chat("p2", "!pick");
        lobby.StartMatch();
        lobby.Sent.Clear();

        lobby.AnswerPicks();

        Assert.Empty(lobby.Sent);
    }

    [Fact]
    public void A_pick_that_no_longer_fits_the_rules_is_dropped()
    {
        var lobby = Create();
        lobby.AnswerPicksAtOnce = false;
        lobby.Chat("p1", "!pick");
        lobby.Run(() => lobby.Engine.ChangeSettings(lobby.Engine.Settings with { Regulation = Rules with { StarMin = 6, StarMax = 7 } }));
        lobby.Sent.Clear();

        lobby.AnswerPicks();

        Assert.Empty(lobby.Sent);
    }

    [Fact]
    public void A_pick_answered_after_picks_were_turned_off_is_dropped()
    {
        var lobby = Create();
        lobby.AnswerPicksAtOnce = false;
        lobby.Chat("p2", "!pick");
        lobby.Run(() => lobby.Engine.ChangeSettings(lobby.Engine.Settings with { MapPick = new MapPickSettings { Enabled = false } }));
        lobby.Sent.Clear();

        lobby.AnswerPicks();

        Assert.Empty(lobby.Sent);
    }

    [Fact]
    public void Nothing_fitting_gets_one_short_reply()
    {
        var lobby = Create();
        lobby.PickResults.Enqueue(new MapPickResult(MapPickOutcome.NoneFit));

        lobby.Chat("p1", "!pick");

        Assert.Equal(["No maps fit the current rules."], lobby.Said);
        Assert.Equal(0, lobby.SentCount<SetBeatmap>());
    }

    [Fact]
    public void An_api_failure_gets_one_short_reply()
    {
        var lobby = Create();
        lobby.PickResults.Enqueue(new MapPickResult(MapPickOutcome.Failed));

        lobby.Chat("p2", "!pick");

        Assert.Equal(["Couldn't reach osu! right now, try again later."], lobby.Said);
    }

    [Fact]
    public void Help_mentions_pick_only_while_it_is_on()
    {
        var off = Create(enabled: false);
        off.Chat("p1", "!help");
        Assert.DoesNotContain(off.Said, s => s.Contains("!pick", StringComparison.Ordinal));

        var on = Create();
        on.Chat("p1", "!help");
        Assert.Contains(on.Said, s => s.Contains("!pick", StringComparison.Ordinal));
    }
}
