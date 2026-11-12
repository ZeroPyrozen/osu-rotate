using OsuRotate.Domain;

namespace OsuRotate.Domain.Tests;

// osu-ahr's LobbyKeeper host-kick counterattack, which needs the match history (phase 4, issue 07).
public class HostKickTests
{
    private static LobbyHarness Create(int tolerance = 4)
    {
        var lobby = new LobbyHarness(s => s with { HostAfkCheckInterval = TimeSpan.Zero, HostKickTolerance = tolerance, AfkKickEnabled = true });
        lobby.AddPlayers("p1", "p2", "p3", "p4", "p5", "p6");
        lobby.Feed(new RoomJoined(1));

        // The first read only finds where the history ends.
        lobby.Feed(new MatchHistoryRead([new SeenKick("p6", lobby.Time.GetUtcNow())], 100));
        lobby.Sent.Clear();
        return lobby;
    }

    private static void Kicked(LobbyHarness lobby, long latest, params string[] names)
    {
        var now = lobby.Time.GetUtcNow();
        lobby.Feed(new MatchHistoryRead([.. names.Select(n => new SeenKick(n, now))], latest));
    }

    private static IEnumerable<string> KicksSent(LobbyHarness lobby) => lobby.Sent.OfType<KickPlayer>().Select(k => k.Username);

    [Fact]
    public void Entering_the_room_reads_the_history_once_to_find_its_end()
    {
        var lobby = new LobbyHarness(s => s with { HostAfkCheckInterval = TimeSpan.Zero });
        lobby.Feed(new RoomCreated(1, "room"));
        Assert.Equal([new ReadMatchHistory(null)], lobby.Sent.OfType<ReadMatchHistory>());
    }

    [Fact]
    public void Kicks_from_before_the_bot_joined_are_not_judged()
    {
        var lobby = Create(tolerance: 1);
        Assert.Empty(KicksSent(lobby));
    }

    [Fact]
    public void A_host_who_kicks_four_players_is_kicked()
    {
        var lobby = Create();
        Kicked(lobby, 101, "p2", "p3", "p4");
        Assert.Empty(KicksSent(lobby));

        Kicked(lobby, 102, "p5");
        Assert.Equal(["p1"], KicksSent(lobby));
    }

    [Fact]
    public void Kicking_the_same_player_twice_counts_once()
    {
        var lobby = Create(tolerance: 2);
        Kicked(lobby, 101, "p2");
        Kicked(lobby, 102, "p2");
        Assert.Empty(KicksSent(lobby));
    }

    [Fact]
    public void Kicking_the_owner_or_a_referee_is_answered_at_once()
    {
        var lobby = Create();
        Kicked(lobby, 101, LobbyHarness.Owner);
        Assert.Equal(["p1"], KicksSent(lobby));

        lobby = Create();
        lobby.Feed(new RefereeAdded("p3"));
        Kicked(lobby, 101, "p3");
        Assert.Equal(["p1"], KicksSent(lobby));
    }

    [Fact]
    public void The_count_starts_over_after_a_counterattack()
    {
        var lobby = Create(tolerance: 2);
        Kicked(lobby, 101, "p2", "p3");
        Assert.Single(KicksSent(lobby));
        lobby.Sent.Clear();

        // p1 is gone and p2 hosts: one kick starts p2's own count.
        Kicked(lobby, 102, "p4");
        Assert.Empty(KicksSent(lobby));
    }

    [Fact]
    public void Each_turn_counts_on_its_own()
    {
        var lobby = Create(tolerance: 3);
        Kicked(lobby, 101, "p4", "p5");
        lobby.Time.Advance(TimeSpan.FromSeconds(10));
        lobby.Run(() => lobby.Engine.Skip());
        lobby.Time.Advance(TimeSpan.FromSeconds(10));
        Kicked(lobby, 102, "p6");

        Assert.Empty(KicksSent(lobby));
    }

    [Fact]
    public void A_kick_counts_against_whoever_hosted_when_it_happened()
    {
        var lobby = Create(tolerance: 1);
        var duringP1 = lobby.Time.GetUtcNow();
        lobby.Time.Advance(TimeSpan.FromSeconds(30));
        lobby.Run(() => lobby.Engine.Skip());
        lobby.AssertHost("p2");

        // Read after p2 took over, but the kick was p1's.
        lobby.Feed(new MatchHistoryRead([new SeenKick("p5", duringP1)], 101));
        Assert.Equal(["p1"], KicksSent(lobby));
    }

    [Fact]
    public void The_bots_own_kicks_do_not_count()
    {
        var lobby = Create(tolerance: 1);
        lobby.Feed(new PlayerKicked("p5"));
        Kicked(lobby, 101, "p5");
        Assert.Empty(KicksSent(lobby));

        // Only that one kick is excused.
        Kicked(lobby, 102, "p5");
        Assert.Equal(["p1"], KicksSent(lobby));
    }

    [Fact]
    public void An_old_referee_kick_does_not_excuse_a_new_one()
    {
        var lobby = Create(tolerance: 1);
        lobby.Feed(new PlayerKicked("p5"));
        lobby.Time.Advance(TimeSpan.FromMinutes(6));
        Kicked(lobby, 101, "p5");
        Assert.Equal(["p1"], KicksSent(lobby));
    }

    [Fact]
    public void The_owner_and_referees_may_kick_as_host()
    {
        var lobby = Create(tolerance: 1);
        lobby.AddPlayers(LobbyHarness.Owner);
        lobby.Run(() => lobby.Engine.SkipTo(LobbyHarness.Owner));
        lobby.AssertHost(LobbyHarness.Owner);
        Kicked(lobby, 101, "p5");
        Assert.Empty(KicksSent(lobby));

        lobby.Feed(new RefereeAdded("p2"));
        lobby.Run(() => lobby.Engine.SkipTo("p2"));
        lobby.Time.Advance(TimeSpan.FromSeconds(1));
        Kicked(lobby, 102, "p6");
        Assert.Empty(KicksSent(lobby));
    }

    [Fact]
    public void Kicks_with_no_host_count_against_nobody()
    {
        var lobby = Create(tolerance: 1);
        lobby.Time.Advance(TimeSpan.FromSeconds(1));
        lobby.Feed(new HostCleared());
        lobby.Time.Advance(TimeSpan.FromSeconds(1));
        Kicked(lobby, 101, "p5");
        Assert.Empty(KicksSent(lobby));
    }

    [Fact]
    public void A_host_who_already_left_is_not_kicked()
    {
        var lobby = Create(tolerance: 1);
        var duringP1 = lobby.Time.GetUtcNow();
        lobby.Time.Advance(TimeSpan.FromSeconds(5));
        lobby.RemovePlayer("p1");
        lobby.Feed(new MatchHistoryRead([new SeenKick("p5", duringP1)], 101));
        Assert.Empty(KicksSent(lobby));
    }

    [Fact]
    public void The_next_read_starts_where_the_last_one_ended()
    {
        var lobby = Create();
        Kicked(lobby, 107, "p2");
        lobby.Feed(new MatchStarted());
        Assert.Equal([new ReadMatchHistory(107)], lobby.Sent.OfType<ReadMatchHistory>());
    }

    [Fact]
    public void Rejoining_finds_the_end_of_the_history_again_without_judging_the_page()
    {
        var lobby = Create(tolerance: 1);
        lobby.Feed(new RoomJoined(1));
        Assert.Contains(new ReadMatchHistory(null), lobby.Sent);

        // The latest page again, with kicks from before: only the new end is taken.
        Kicked(lobby, 120, "p5");
        Assert.Empty(KicksSent(lobby));
        Kicked(lobby, 121, "p6");
        Assert.Equal(["p1"], KicksSent(lobby));
    }

    [Fact]
    public void A_page_already_seen_is_ignored()
    {
        var lobby = Create(tolerance: 1);
        Kicked(lobby, 100, "p5");
        Kicked(lobby, 99, "p5");
        Assert.Empty(KicksSent(lobby));
    }

    [Fact]
    public void Zero_tolerance_turns_it_off()
    {
        var lobby = new LobbyHarness(s => s with { HostAfkCheckInterval = TimeSpan.Zero, HostKickTolerance = 0 });
        lobby.AddPlayers("p1", "p2");
        lobby.Feed(new RoomJoined(1));
        Assert.Empty(lobby.Sent.OfType<ReadMatchHistory>());

        lobby.Feed(new MatchHistoryRead([], 100));
        lobby.Feed(new MatchHistoryRead([new SeenKick(LobbyHarness.Owner, lobby.Time.GetUtcNow())], 101));
        lobby.Wait(TimeSpan.FromMinutes(5));
        Assert.Empty(KicksSent(lobby));
        Assert.Empty(lobby.Sent.OfType<ReadMatchHistory>());
    }

    [Fact]
    public void The_history_is_read_when_a_match_starts_from_where_it_ended()
    {
        var lobby = Create();
        lobby.Feed(new MatchStarted());
        Assert.Equal([new ReadMatchHistory(100)], lobby.Sent.OfType<ReadMatchHistory>());
    }

    [Fact]
    public void Between_matches_the_history_is_read_every_minute_but_not_during_one()
    {
        var lobby = Create();
        lobby.Wait(TimeSpan.FromSeconds(59));
        Assert.Empty(lobby.Sent.OfType<ReadMatchHistory>());
        lobby.Wait(TimeSpan.FromSeconds(1));
        Assert.Equal([new ReadMatchHistory(100)], lobby.Sent.OfType<ReadMatchHistory>());

        lobby.Feed(new MatchStarted());
        lobby.Sent.Clear();
        lobby.Wait(TimeSpan.FromMinutes(3));
        Assert.Empty(lobby.Sent.OfType<ReadMatchHistory>());

        lobby.Feed(new MatchFinished());
        lobby.Wait(TimeSpan.FromMinutes(1));
        Assert.NotEmpty(lobby.Sent.OfType<ReadMatchHistory>());
    }

    [Fact]
    public void Until_a_first_read_comes_back_the_timer_asks_from_the_start_and_a_match_does_not_ask()
    {
        var lobby = new LobbyHarness(s => s with { HostAfkCheckInterval = TimeSpan.Zero });
        lobby.AddPlayers("p1", "p2");
        lobby.Feed(new RoomJoined(1));
        lobby.Sent.Clear();
        lobby.Wait(TimeSpan.FromMinutes(1));
        Assert.Equal([new ReadMatchHistory(null)], lobby.Sent.OfType<ReadMatchHistory>());

        lobby.Sent.Clear();
        lobby.Feed(new MatchStarted());
        Assert.Empty(lobby.Sent.OfType<ReadMatchHistory>());
    }
}
