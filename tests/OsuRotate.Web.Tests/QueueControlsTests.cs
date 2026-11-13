using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OsuRotate.Application;
using OsuRotate.Domain;
using OsuRotate.Web.Bot;
using OsuRotate.Web.Components.Lobby;

namespace OsuRotate.Web.Tests;

// Phase 5, issue 04: queue and host controls (UC5, UC6).
public class QueueControlsTests
{
    private static LobbySnapshot Snapshot(
        LobbyStatus status = LobbyStatus.Open,
        string[]? queue = null,
        bool matching = false,
        bool countdown = false,
        int players = 3,
        string? host = "p1") =>
        new(status, true, 7, "room", false,
            [.. Enumerable.Range(1, players).Select(i => new PlayerSnapshot($"p{i}", null, i, Team.None, MatchStatus.InLobby))],
            host, queue ?? ["p1", "p2", "p3"], matching, countdown, 0, [], [], new LobbySettings { OwnerName = "creator" });

    public class Rules
    {
        [Theory]
        [InlineData("p2", -1, "p2,p1,p3")]
        [InlineData("p2", 1, "p1,p3,p2")]
        [InlineData("p1", 1, "p2,p1,p3")]
        [InlineData("p3", -1, "p1,p3,p2")]
        public void A_move_swaps_with_the_neighbour(string name, int direction, string expected)
        {
            Assert.Equal(expected.Split(','), QueueActions.Moved(["p1", "p2", "p3"], name, direction));
        }

        [Theory]
        [InlineData("p1", -1)]
        [InlineData("p3", 1)]
        [InlineData("p9", 1)]
        [InlineData("p2", 2)]
        [InlineData("p2", 0)]
        public void Moves_off_the_ends_or_of_strangers_are_not_moves(string name, int direction)
        {
            Assert.Null(QueueActions.Moved(["p1", "p2", "p3"], name, direction));
        }

        [Fact]
        public void A_move_is_only_ever_one_place()
        {
            Assert.Null(QueueActions.Moved(["p1", "p2", "p3", "p4"], "p1", 2));
            Assert.Null(QueueActions.Moved(["p1", "p2", "p3", "p4"], "p4", -2));
        }

        [Theory]
        [InlineData(null, 0)]
        [InlineData("", 0)]
        [InlineData(" ", 0)]
        [InlineData("0", 0)]
        [InlineData("30", 30)]
        [InlineData(" 90 ", 90)]
        [InlineData("300", 300)]
        public void Countdowns_from_zero_to_five_minutes_are_accepted(string? text, int expected)
        {
            Assert.True(QueueActions.TryParseCountdown(text, out var seconds));
            Assert.Equal(expected, seconds);
        }

        [Theory]
        [InlineData("301")]
        [InlineData("-5")]
        [InlineData("1.5")]
        [InlineData("soon")]
        [InlineData("٣٠")]
        public void Other_countdowns_are_refused(string text)
        {
            Assert.False(QueueActions.TryParseCountdown(text, out _));
        }

        [Fact]
        public void An_open_lobby_between_matches_can_start_skip_and_reorder()
        {
            var snapshot = Snapshot();
            Assert.True(QueueActions.CanStart(snapshot));
            Assert.True(QueueActions.CanSkip(snapshot));
            Assert.True(QueueActions.CanReorder(snapshot));
            Assert.False(QueueActions.CanAbort(snapshot));
            Assert.False(QueueActions.CanCancelCountdown(snapshot));
        }

        [Fact]
        public void During_a_match_only_abort_is_new()
        {
            var snapshot = Snapshot(matching: true, countdown: true);
            Assert.True(QueueActions.CanAbort(snapshot));
            Assert.False(QueueActions.CanStart(snapshot));
            Assert.False(QueueActions.CanCancelCountdown(snapshot));
        }

        [Fact]
        public void A_running_countdown_can_be_stopped()
        {
            Assert.True(QueueActions.CanCancelCountdown(Snapshot(countdown: true)));
        }

        [Fact]
        public void One_player_cannot_be_reordered_or_skipped_and_an_empty_room_cannot_start()
        {
            var alone = Snapshot(queue: ["p1"], players: 1);
            Assert.False(QueueActions.CanReorder(alone));
            Assert.False(QueueActions.CanSkip(alone));
            Assert.True(QueueActions.CanStart(alone));
            Assert.False(QueueActions.CanStart(Snapshot(queue: [], players: 0, host: null)));
        }

        [Theory]
        [InlineData(LobbyStatus.Idle)]
        [InlineData(LobbyStatus.Syncing)]
        [InlineData(LobbyStatus.Reconnecting)]
        [InlineData(LobbyStatus.Closing)]
        public void Nothing_works_unless_the_lobby_is_open(LobbyStatus status)
        {
            var snapshot = Snapshot(status, matching: status == LobbyStatus.Reconnecting, countdown: true);
            Assert.False(QueueActions.CanStart(snapshot));
            Assert.False(QueueActions.CanSkip(snapshot));
            Assert.False(QueueActions.CanReorder(snapshot));
            Assert.False(QueueActions.CanAbort(snapshot));
            Assert.False(QueueActions.CanCancelCountdown(snapshot));
            Assert.False(QueueActions.CanStart(null));
        }
    }

    public class Actions
    {
        [Fact]
        public async Task Moving_a_player_reorders_the_queue()
        {
            await using var lobby = new TestLobby();
            await lobby.OpenAsync("p1", "p2", "p3");

            Assert.Null(await new QueueActions(lobby.Runtime).MoveAsync("p3", -1));
            await TestLobby.WaitUntilAsync(() => lobby.Snapshot.Queue.SequenceEqual(["p1", "p3", "p2"]));
        }

        [Fact]
        public async Task Moving_a_player_to_the_front_hands_them_host()
        {
            await using var lobby = new TestLobby();
            await lobby.OpenAsync("p1", "p2", "p3");

            Assert.Null(await new QueueActions(lobby.Runtime).MoveAsync("p2", -1));
            await TestLobby.WaitUntilAsync(() => lobby.Transport.Sent.Contains(new TransferHost("p2", 22)));
        }

        [Fact]
        public async Task A_move_off_the_end_is_refused()
        {
            await using var lobby = new TestLobby();
            await lobby.OpenAsync("p1", "p2");

            Assert.Equal("That player cannot move there.", await new QueueActions(lobby.Runtime).MoveAsync("p2", 1));
        }

        [Fact]
        public async Task Skip_passes_host_to_the_next_player()
        {
            await using var lobby = new TestLobby();
            await lobby.OpenAsync("p1", "p2", "p3");

            Assert.Null(await new QueueActions(lobby.Runtime).SkipHostAsync());
            await TestLobby.WaitUntilAsync(() => lobby.Transport.Sent.Contains(new TransferHost("p2", 22)));
        }

        [Fact]
        public async Task Give_host_skips_to_that_player()
        {
            await using var lobby = new TestLobby();
            await lobby.OpenAsync("p1", "p2", "p3");

            Assert.Null(await new QueueActions(lobby.Runtime).GiveHostAsync("p3"));
            await TestLobby.WaitUntilAsync(() => lobby.Transport.Sent.Contains(new TransferHost("p3", 33)));
        }

        [Fact]
        public async Task Host_cannot_be_given_to_someone_not_queued()
        {
            await using var lobby = new TestLobby();
            await lobby.OpenAsync("p1", "p2");

            Assert.Contains("not possible right now", await new QueueActions(lobby.Runtime).GiveHostAsync("stranger"), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Start_now_and_with_a_countdown()
        {
            await using var lobby = new TestLobby();
            await lobby.OpenAsync("p1", "p2");
            var actions = new QueueActions(lobby.Runtime);

            Assert.Null(await actions.StartAsync("90"));
            await TestLobby.WaitUntilAsync(() => lobby.Snapshot.CountdownRunning);
            Assert.Null(await actions.CancelCountdownAsync());
            await TestLobby.WaitUntilAsync(() => !lobby.Snapshot.CountdownRunning);

            Assert.Null(await actions.StartAsync(""));
            await TestLobby.WaitUntilAsync(() => lobby.Transport.Sent.Contains(new StartMatch()));
        }

        [Fact]
        public async Task A_bad_countdown_is_refused_before_anything_is_sent()
        {
            await using var lobby = new TestLobby();
            await lobby.OpenAsync("p1", "p2");

            Assert.Contains("0 to 300", await new QueueActions(lobby.Runtime).StartAsync("soon"), StringComparison.Ordinal);
            await lobby.Actor.DrainAsync().WaitAsync(TestLobby.Timeout);
            Assert.DoesNotContain(lobby.Transport.Sent, c => c is StartMatch);
        }

        [Fact]
        public async Task Abort_aborts_the_running_match()
        {
            await using var lobby = new TestLobby();
            await lobby.OpenAsync("p1", "p2");
            lobby.Transport.Emit(new MatchStarted());
            await TestLobby.WaitUntilAsync(() => lobby.Snapshot.IsMatching);

            Assert.Null(await new QueueActions(lobby.Runtime).AbortAsync());
            await TestLobby.WaitUntilAsync(() => lobby.Transport.Sent.Contains(new AbortMatch()));
        }

        [Fact]
        public async Task Actions_the_lobby_has_moved_past_are_refused()
        {
            await using var lobby = new TestLobby();
            await lobby.OpenAsync("p1", "p2");
            var actions = new QueueActions(lobby.Runtime);

            Assert.Contains("not possible right now", await actions.AbortAsync(), StringComparison.Ordinal);
            Assert.Contains("not possible right now", await actions.CancelCountdownAsync(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Before_the_bot_runs_every_action_says_so()
        {
            var actions = new QueueActions(new LobbyRuntime());
            Assert.Equal(LifecycleActions.NotRunning, await actions.MoveAsync("p1", 1));
            Assert.Equal(LifecycleActions.NotRunning, await actions.SkipHostAsync());
            Assert.Equal(LifecycleActions.NotRunning, await actions.GiveHostAsync("p1"));
            Assert.Equal(LifecycleActions.NotRunning, await actions.StartAsync(null));
            Assert.Equal(LifecycleActions.NotRunning, await actions.AbortAsync());
            Assert.Equal(LifecycleActions.NotRunning, await actions.CancelCountdownAsync());
        }
    }

    public sealed class Rendering : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly HtmlRenderer _renderer;

        public Rendering()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<LobbyRuntime>();
            _services = services.BuildServiceProvider();
            _renderer = new HtmlRenderer(_services, _services.GetRequiredService<ILoggerFactory>());
        }

        public async ValueTask DisposeAsync()
        {
            await _renderer.DisposeAsync();
            await _services.DisposeAsync();
        }

        private Task<string> RenderAsync(LobbySnapshot? snapshot) =>
            _renderer.Dispatcher.InvokeAsync(async () =>
                (await _renderer.RenderComponentAsync<QueueControls>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Snapshot"] = snapshot }))).ToHtmlString());

        [Fact]
        public async Task Between_matches_it_offers_start_and_skip()
        {
            var html = await RenderAsync(Snapshot());
            Assert.Contains("Start match", html, StringComparison.Ordinal);
            Assert.Contains("Skip the host", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Abort the match", html, StringComparison.Ordinal);
            Assert.Contains("Leave it empty to start now.", html, StringComparison.Ordinal);

            // Moving players in the queue is on each player's row in the player list now.
            Assert.DoesNotContain("Give host", html, StringComparison.Ordinal);
        }

        [Fact]
        public async Task During_a_match_it_offers_abort()
        {
            var html = await RenderAsync(Snapshot(matching: true));
            Assert.Contains("Abort the match", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Start match", html, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_countdown_can_be_stopped()
        {
            Assert.Contains("Stop the countdown", await RenderAsync(Snapshot(countdown: true)), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Nothing_shows_unless_the_lobby_is_open()
        {
            Assert.Equal(string.Empty, (await RenderAsync(Snapshot(LobbyStatus.Idle))).Trim());
            Assert.Equal(string.Empty, (await RenderAsync(null)).Trim());
        }
    }
}
