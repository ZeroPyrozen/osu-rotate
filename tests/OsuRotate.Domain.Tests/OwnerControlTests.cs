using OsuRotate.Domain;

namespace OsuRotate.Domain.Tests;

// The dashboard forcing the match (phase 5, issue 04): like *start, *abort and !stop.
public class OwnerControlTests
{
    private static LobbyHarness Create()
    {
        var lobby = new LobbyHarness(s => s with { HostAfkCheckInterval = TimeSpan.Zero, Regulation = s.Regulation with { Enabled = false } });
        lobby.AddPlayers("p1", "p2", "p3");
        lobby.Sent.Clear();
        return lobby;
    }

    [Fact]
    public void Start_now_starts_the_match()
    {
        var lobby = Create();
        lobby.Run(() => lobby.Engine.ForceStart(0));
        Assert.Contains(new StartMatch(), lobby.Sent);
        Assert.True(lobby.IsMatching);
    }

    [Fact]
    public void A_countdown_starts_the_timer_and_the_match_when_it_runs_out()
    {
        var lobby = Create();
        lobby.Run(() => lobby.Engine.ForceStart(60));
        Assert.True(lobby.Engine.IsStartTimerRunning);
        Assert.DoesNotContain(new StartMatch(), lobby.Sent);

        lobby.Wait(TimeSpan.FromSeconds(60));
        Assert.Contains(new StartMatch(), lobby.Sent);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(LobbyEngine.MaxCountdownSeconds + 1)]
    public void A_countdown_out_of_range_does_nothing(int seconds)
    {
        var lobby = Create();
        lobby.Run(() => lobby.Engine.ForceStart(seconds));
        Assert.Empty(lobby.Sent);
        Assert.False(lobby.Engine.IsStartTimerRunning);
    }

    [Fact]
    public void The_longest_countdown_is_allowed()
    {
        var lobby = Create();
        lobby.Run(() => lobby.Engine.ForceStart(LobbyEngine.MaxCountdownSeconds));
        Assert.True(lobby.Engine.IsStartTimerRunning);
    }

    [Fact]
    public void Starting_during_a_match_does_nothing()
    {
        var lobby = Create();
        lobby.Feed(new MatchStarted());
        lobby.Sent.Clear();
        lobby.Run(() => lobby.Engine.ForceStart(0));
        Assert.Empty(lobby.Sent);
    }

    [Fact]
    public void Abort_aborts_a_running_match_only()
    {
        var lobby = Create();
        lobby.Run(() => lobby.Engine.ForceAbort());
        Assert.Empty(lobby.Sent);

        lobby.Feed(new MatchStarted());
        lobby.Run(() => lobby.Engine.ForceAbort());
        Assert.Contains(new AbortMatch(), lobby.Sent);
    }

    [Fact]
    public void Cancelling_the_countdown_stops_it_and_says_so()
    {
        var lobby = Create();
        lobby.Run(() => lobby.Engine.ForceStart(60));
        lobby.Run(() => lobby.Engine.CancelCountdown());

        Assert.False(lobby.Engine.IsStartTimerRunning);
        Assert.Contains("Aborted the match start timer.", lobby.Said);
        lobby.Wait(TimeSpan.FromSeconds(90));
        Assert.DoesNotContain(new StartMatch(), lobby.Sent);
    }

    [Fact]
    public void Cancelling_with_no_countdown_says_nothing()
    {
        var lobby = Create();
        lobby.Run(() => lobby.Engine.CancelCountdown());
        Assert.Empty(lobby.Sent);
    }
}
