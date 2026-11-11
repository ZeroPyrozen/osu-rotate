using OsuRotate.Domain;

namespace OsuRotate.Domain.Tests;

public class TimerTests
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    public class Timers_
    {
        private readonly ManualTime _time = new();
        private readonly List<string> _fired = [];
        private readonly Timers _timers;

        public Timers_() => _timers = new Timers(_time);

        [Fact]
        public void Nothing_is_due_when_no_timer_runs()
        {
            Assert.Null(_timers.NextDue);
            _timers.FireDue();
            Assert.Empty(_fired);
        }

        [Fact]
        public void A_timer_fires_once_when_due_and_not_before()
        {
            _timers.Start("a", 5 * Second, () => _fired.Add("a"));
            Assert.Equal(_time.GetUtcNow() + 5 * Second, _timers.NextDue);

            _time.Advance(4 * Second);
            _timers.FireDue();
            Assert.Empty(_fired);

            _time.Advance(Second);
            _timers.FireDue();
            _timers.FireDue();
            Assert.Equal(["a"], _fired);
            Assert.False(_timers.IsRunning("a"));
            Assert.Null(_timers.NextDue);
        }

        [Fact]
        public void Starting_a_running_timer_again_restarts_it()
        {
            _timers.Start("a", 5 * Second, () => _fired.Add("first"));
            _time.Advance(3 * Second);
            _timers.Start("a", 5 * Second, () => _fired.Add("second"));

            _time.Advance(3 * Second);
            _timers.FireDue();
            Assert.Empty(_fired);

            _time.Advance(2 * Second);
            _timers.FireDue();
            Assert.Equal(["second"], _fired);
        }

        [Fact]
        public void A_cancelled_timer_never_fires()
        {
            _timers.Start("a", Second, () => _fired.Add("a"));
            _timers.Cancel("a");
            _time.Advance(2 * Second);
            _timers.FireDue();
            Assert.Empty(_fired);
        }

        [Fact]
        public void Due_timers_fire_in_due_order_and_a_timer_can_cancel_a_later_one()
        {
            _timers.Start("late", 2 * Second, () => _fired.Add("late"));
            _timers.Start("early", Second, () => _fired.Add("early"));
            _timers.Start("same", 2 * Second, () => _fired.Add("same"));
            _timers.Start("cancelled", 3 * Second, () => _fired.Add("cancelled"));
            _timers.Start("canceller", 2 * Second, () => _timers.Cancel("cancelled"));

            _time.Advance(10 * Second);
            _timers.FireDue();
            Assert.Equal(["early", "late", "same"], _fired);
        }

        [Fact]
        public void A_timer_started_while_firing_waits_for_its_own_due_time()
        {
            _timers.Start("a", Second, () => _timers.Start("b", Second, () => _fired.Add("b")));
            _time.Advance(Second);
            _timers.FireDue();
            Assert.Empty(_fired);
            Assert.True(_timers.IsRunning("b"));
        }
    }

    // osu-ahr's transferhost_timeout_ms: when Bancho has not confirmed a host transfer after 5 seconds
    // and the player is still in the room, the bot asks for !mp settings.
    public class HostTransferTimeout
    {
        private readonly LobbyHarness _lobby = new(s => s with { HostAfkCheckInterval = TimeSpan.Zero });

        private int RoomStateRequests => _lobby.Sent.OfType<RequestRoomState>().Count();

        [Fact]
        public void An_unconfirmed_transfer_asks_for_the_room_state_after_5_seconds()
        {
            _lobby.Holding = true;
            _lobby.AddPlayers("p1");
            _lobby.Drop();
            Assert.Equal("p1", _lobby.Engine.Room.PendingHost?.Name);
            Assert.Equal(_lobby.Time.GetUtcNow() + 5 * Second, _lobby.Engine.NextDue);

            _lobby.Wait(4.9 * Second);
            Assert.Equal(0, RoomStateRequests);

            _lobby.Wait(0.1 * Second);
            Assert.Equal(1, RoomStateRequests);

            // The room state names the real host, so the bot recovers and stops waiting.
            _lobby.AssertHost("p1");
            Assert.Null(_lobby.Engine.Room.PendingHost);
            Assert.Null(_lobby.Engine.NextDue);
        }

        [Fact]
        public void A_confirmed_transfer_stops_the_timer()
        {
            _lobby.AddPlayers("p1");
            _lobby.AssertHost("p1");
            Assert.Null(_lobby.Engine.NextDue);

            _lobby.Wait(10 * Second);
            Assert.Equal(0, RoomStateRequests);
        }

        [Fact]
        public void A_pending_host_who_leaves_needs_no_room_state()
        {
            _lobby.AddPlayers("p1", "p2");
            _lobby.Holding = true;
            _lobby.PlayMatch();
            Assert.Equal("p2", _lobby.Engine.Room.PendingHost?.Name);
            _lobby.Drop();

            _lobby.RemovePlayer("p2");
            _lobby.Wait(10 * Second);
            Assert.Equal(0, RoomStateRequests);
        }

        [Fact]
        public void Bancho_not_finding_the_player_stops_the_timer()
        {
            _lobby.Holding = true;
            _lobby.AddPlayers("p1");
            _lobby.Drop();
            Assert.Equal("p1", _lobby.Engine.Room.PendingHost?.Name);

            _lobby.Feed(new UserNotFound());
            _lobby.Wait(10 * Second);
            Assert.Equal(0, RoomStateRequests);
        }

        [Fact]
        public void Each_transfer_gets_its_own_5_seconds()
        {
            _lobby.AddPlayers("p1", "p2", "p3");
            _lobby.Holding = true;
            _lobby.Run(_lobby.Engine.Skip);
            _lobby.Drop();
            _lobby.Wait(3 * Second);

            _lobby.Holding = true;
            _lobby.Run(_lobby.Engine.Skip);
            _lobby.Drop();
            _lobby.Wait(3 * Second);
            Assert.Equal(0, RoomStateRequests);

            _lobby.Wait(2 * Second);
            Assert.Equal(1, RoomStateRequests);
        }
    }
}
