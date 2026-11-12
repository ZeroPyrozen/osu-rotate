using OsuRotate.Domain;

namespace OsuRotate.Domain.Tests;

// The engine's report of each match for the history (phase 4, issue 05).
public class MatchRecordTests
{
    private static LobbyHarness Create()
    {
        var lobby = new LobbyHarness(s => s with { HostAfkCheckInterval = TimeSpan.Zero, Regulation = s.Regulation with { Enabled = false } });
        lobby.ApplyRoomState(RoomStates.Case1_1);
        lobby.PickMap(4242);
        lobby.Sent.Clear();
        return lobby;
    }

    private static MatchRecord Recorded(LobbyHarness lobby) => Assert.Single(lobby.Sent.OfType<RecordMatch>()).Match;

    [Fact]
    public void A_finished_match_is_recorded_with_its_host_map_times_and_scores()
    {
        var lobby = Create();
        var start = lobby.Time.GetUtcNow();
        lobby.Feed(new MatchStarted());
        lobby.Time.Advance(TimeSpan.FromMinutes(2));
        lobby.FinishMatchWith(("p2", 500_000), ("p1", 812_345), ("p3", 0));

        var match = Recorded(lobby);
        Assert.Equal(4242, match.BeatmapId);
        Assert.Equal("p1", match.HostName);
        Assert.Equal(111, match.HostUserId);
        Assert.Equal(start, match.StartedAt);
        Assert.Equal(start + TimeSpan.FromMinutes(2), match.EndedAt);
        Assert.Equal(MatchOutcome.Finished, match.Outcome);
        Assert.Equal(
            [new MatchRecordScore("p2", 222, 500_000, true), new MatchRecordScore("p1", 111, 812_345, true), new MatchRecordScore("p3", 333, 0, false)],
            match.Scores);
    }

    [Fact]
    public void The_host_is_who_started_it_even_after_host_moves_on()
    {
        var lobby = Create();
        lobby.Feed(new MatchStarted());
        lobby.FinishMatchWith(("p1", 1));

        Assert.Equal("p1", Recorded(lobby).HostName);
        Assert.Equal("p2", lobby.Engine.Room.Host?.Name);
    }

    [Fact]
    public void Players_who_never_finished_have_no_score()
    {
        var lobby = Create();
        lobby.Feed(new MatchStarted());
        lobby.FinishMatchWith(("p4", 300));

        Assert.Equal(["p4"], Recorded(lobby).Scores.Select(s => s.Username));
    }

    [Fact]
    public void An_aborted_match_is_recorded_with_the_scores_so_far()
    {
        var lobby = Create();
        lobby.Feed(new MatchStarted());
        lobby.Feed(new PlayerFinished("p2", 100, true));
        lobby.Feed(new MatchAborted());

        var match = Recorded(lobby);
        Assert.Equal(MatchOutcome.Aborted, match.Outcome);
        Assert.Equal([new MatchRecordScore("p2", 222, 100, true)], match.Scores);
    }

    [Fact]
    public void A_not_in_progress_reply_outside_a_match_records_nothing()
    {
        var lobby = Create();
        lobby.Feed(new MatchNotInProgress());
        lobby.Feed(new MatchFinished());
        Assert.Empty(lobby.Sent.OfType<RecordMatch>());
    }

    [Fact]
    public void Each_match_starts_with_no_scores()
    {
        var lobby = Create();
        lobby.Feed(new MatchStarted());
        lobby.FinishMatchWith(("p1", 1), ("p2", 2));
        lobby.PickMap(4343);
        lobby.Feed(new MatchStarted());
        lobby.FinishMatchWith(("p3", 3));

        var matches = lobby.Sent.OfType<RecordMatch>().Select(r => r.Match).ToList();
        Assert.Equal(2, matches.Count);
        Assert.Equal(["p3"], matches[1].Scores.Select(s => s.Username));
        Assert.Equal(4343, matches[1].BeatmapId);
        Assert.Equal("p2", matches[1].HostName);
    }

    [Fact]
    public void A_match_whose_end_was_missed_does_not_leak_scores_into_the_next()
    {
        var lobby = Create();
        lobby.Feed(new MatchStarted());
        lobby.Feed(new PlayerFinished("p1", 10, true));

        // The end was lost, say in a dropped connection, and the next match starts.
        lobby.Feed(new MatchStarted());
        lobby.FinishMatchWith(("p2", 20));

        Assert.Equal(["p2"], Recorded(lobby).Scores.Select(s => s.Username));
    }

    [Fact]
    public void A_repeated_end_records_the_match_once()
    {
        var lobby = Create();
        lobby.Feed(new MatchStarted());
        lobby.FinishMatchWith(("p1", 1));
        lobby.Feed(new MatchFinished());

        Assert.Single(lobby.Sent.OfType<RecordMatch>());
    }

    [Fact]
    public void A_score_reported_twice_counts_once_with_the_latest()
    {
        var lobby = Create();
        lobby.Feed(new MatchStarted());
        lobby.Feed(new PlayerFinished("p1", 10, false));
        lobby.FinishMatchWith(("p1", 20));

        Assert.Equal([new MatchRecordScore("p1", 111, 20, true)], Recorded(lobby).Scores);
    }

    [Fact]
    public void A_match_with_no_host_is_recorded_without_one()
    {
        var lobby = Create();
        lobby.Feed(new HostCleared());
        lobby.Feed(new MatchStarted());
        lobby.FinishMatchWith(("p1", 1));

        var match = Recorded(lobby);
        Assert.Null(match.HostName);
        Assert.Null(match.HostUserId);
    }
}
