using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OsuRotate.Application;
using OsuRotate.Domain;
using OsuRotate.Web.Bot;
using OsuRotate.Web.Components.Lobby;

namespace OsuRotate.Web.Tests;

// Phase 5, issue 03: create, attach and close from the dashboard (UC1 to UC3).
public class LifecycleControlsTests
{
    private static LobbySnapshot Snapshot(LobbyStatus status, long? roomId = null, bool closing = false, string? keptTitle = null) =>
        new(status, true, roomId, "room", closing, [], null, [], false, false, 0, [], [],
            new LobbySettings { OwnerName = "creator", Keeping = new RoomKeeping { Title = keptTitle } });

    public class Rules
    {
        [Theory]
        [InlineData("123456", 123456)]
        [InlineData(" 123456 ", 123456)]
        [InlineData("#123456", 123456)]
        [InlineData("https://osu.ppy.sh/mp/123456", 123456)]
        [InlineData("http://osu.ppy.sh/mp/123456/", 123456)]
        [InlineData("osu.ppy.sh/mp/123456", 123456)]
        [InlineData("https://osu.ppy.sh/community/matches/53084403", 53084403)]
        [InlineData("HTTPS://OSU.PPY.SH/MP/7", 7)]
        public void Room_numbers_and_links_are_understood(string text, long expected)
        {
            Assert.True(LifecycleActions.TryParseRoom(text, out var roomId));
            Assert.Equal(expected, roomId);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("0")]
        [InlineData("-5")]
        [InlineData("abc")]
        [InlineData("https://example.com/mp/123")]
        [InlineData("https://osu.ppy.sh/users/123")]
        [InlineData("12 34")]
        [InlineData("99999999999999999999")]
        [InlineData("١٢٣")]
        public void Anything_else_is_not_a_room(string? text)
        {
            Assert.False(LifecycleActions.TryParseRoom(text, out _));
        }

        [Theory]
        [InlineData(LobbyStatus.Idle, null, true, false, false)]
        [InlineData(LobbyStatus.Idle, 7L, false, false, false)]
        [InlineData(LobbyStatus.Connecting, null, false, true, false)]
        [InlineData(LobbyStatus.Creating, null, false, true, false)]
        [InlineData(LobbyStatus.Rejoining, 7L, false, true, false)]
        [InlineData(LobbyStatus.Syncing, 7L, false, true, false)]
        [InlineData(LobbyStatus.Open, 7L, false, true, true)]
        [InlineData(LobbyStatus.Reconnecting, 7L, false, true, false)]
        [InlineData(LobbyStatus.Closing, 7L, false, false, false)]
        public void Each_state_offers_only_what_it_allows(LobbyStatus status, long? roomId, bool open, bool close, bool closeWhenEmpty)
        {
            var snapshot = Snapshot(status, roomId);
            Assert.Equal(open, LifecycleActions.CanOpen(snapshot));
            Assert.Equal(close, LifecycleActions.CanClose(snapshot));
            Assert.Equal(closeWhenEmpty, LifecycleActions.CanCloseWhenEmpty(snapshot));
        }

        [Fact]
        public void Nothing_is_offered_before_the_bot_starts()
        {
            Assert.False(LifecycleActions.CanOpen(null));
            Assert.False(LifecycleActions.CanClose(null));
            Assert.False(LifecycleActions.CanCloseWhenEmpty(null));
        }

        [Fact]
        public void A_new_room_takes_the_kept_title()
        {
            Assert.Equal("auto host rotate", LifecycleActions.DefaultTitle(Snapshot(LobbyStatus.Idle, keptTitle: "auto host rotate")));
            Assert.Equal(LobbyLifecycle.DefaultTitle, LifecycleActions.DefaultTitle(Snapshot(LobbyStatus.Idle)));
            Assert.Equal(LobbyLifecycle.DefaultTitle, LifecycleActions.DefaultTitle(Snapshot(LobbyStatus.Idle, keptTitle: "")));
            Assert.Equal(LobbyLifecycle.DefaultTitle, LifecycleActions.DefaultTitle(null));
        }
    }

    public class Actions
    {
        [Fact]
        public async Task Create_makes_a_room_with_the_given_title()
        {
            await using var lobby = new TestLobby();
            await lobby.ConnectAsync();

            Assert.Null(await new LifecycleActions(lobby.Runtime).CreateAsync("  5* rotate  "));
            await TestLobby.WaitUntilAsync(() => lobby.Transport.Sent.Contains(new CreateRoom("5* rotate")));
            Assert.Equal(LobbyStatus.Creating, lobby.Snapshot.Status);
        }

        [Fact]
        public async Task Create_with_no_title_uses_the_kept_one()
        {
            await using var lobby = new TestLobby(s => s with { Keeping = new RoomKeeping { Title = "kept title" } });
            await lobby.ConnectAsync();

            Assert.Null(await new LifecycleActions(lobby.Runtime).CreateAsync("   "));
            await TestLobby.WaitUntilAsync(() => lobby.Transport.Sent.Contains(new CreateRoom("kept title")));
        }

        [Fact]
        public async Task Spaces_around_a_full_length_title_do_not_count_against_it()
        {
            await using var lobby = new TestLobby();
            await lobby.ConnectAsync();
            var title = new string('x', RoomKeeping.MaxTitleLength);

            Assert.Null(await new LifecycleActions(lobby.Runtime).CreateAsync($"  {title}  "));
            await TestLobby.WaitUntilAsync(() => lobby.Transport.Sent.Contains(new CreateRoom(title)));
        }

        [Fact]
        public async Task A_title_that_is_too_long_is_refused()
        {
            await using var lobby = new TestLobby();
            var message = await new LifecycleActions(lobby.Runtime).CreateAsync(new string('x', 51));

            Assert.Contains("50 characters", message, StringComparison.Ordinal);
            await lobby.Actor.DrainAsync().WaitAsync(TestLobby.Timeout);
            Assert.Equal(LobbyStatus.Idle, lobby.Snapshot.Status);
        }

        [Fact]
        public async Task Attach_joins_the_room_from_a_link()
        {
            await using var lobby = new TestLobby();
            await lobby.ConnectAsync();

            Assert.Null(await new LifecycleActions(lobby.Runtime).AttachAsync("https://osu.ppy.sh/mp/424242"));
            await TestLobby.WaitUntilAsync(() => lobby.Transport.Sent.Contains(new JoinRoom(424242)));
            Assert.Equal(424242, lobby.Snapshot.RoomId);
        }

        [Fact]
        public async Task An_unreadable_room_is_refused_with_help()
        {
            await using var lobby = new TestLobby();
            var message = await new LifecycleActions(lobby.Runtime).AttachAsync("my room");

            Assert.Contains("https://osu.ppy.sh/mp/", message, StringComparison.Ordinal);
            await lobby.Actor.DrainAsync().WaitAsync(TestLobby.Timeout);
            Assert.Null(lobby.Snapshot.RoomId);
        }

        [Fact]
        public async Task Close_closes_the_open_room()
        {
            await using var lobby = new TestLobby();
            await lobby.OpenAsync("p1", "p2");

            Assert.Null(await new LifecycleActions(lobby.Runtime).CloseAsync());
            await TestLobby.WaitUntilAsync(() => lobby.Transport.Sent.Contains(new CloseRoom()));
            Assert.Equal(LobbyStatus.Closing, lobby.Snapshot.Status);
        }

        [Fact]
        public async Task Close_when_empty_can_be_turned_on_and_off()
        {
            await using var lobby = new TestLobby();
            await lobby.OpenAsync("p1");
            var actions = new LifecycleActions(lobby.Runtime);

            Assert.Null(await actions.CloseWhenEmptyAsync(true));
            await TestLobby.WaitUntilAsync(() => lobby.Snapshot.ClosingWhenEmpty);
            Assert.Null(await actions.CloseWhenEmptyAsync(false));
            await TestLobby.WaitUntilAsync(() => !lobby.Snapshot.ClosingWhenEmpty);
        }

        [Fact]
        public async Task An_action_the_lobby_has_moved_past_is_refused()
        {
            await using var lobby = new TestLobby();
            await lobby.OpenAsync("p1");
            var actions = new LifecycleActions(lobby.Runtime);

            // Open already: a page that still shows Idle cannot create or attach.
            Assert.Contains("not possible right now", await actions.CreateAsync("x"), StringComparison.Ordinal);
            Assert.Contains("not possible right now", await actions.AttachAsync("5"), StringComparison.Ordinal);
            await lobby.Actor.DrainAsync().WaitAsync(TestLobby.Timeout);
            Assert.DoesNotContain(lobby.Transport.Sent, c => c is CreateRoom or JoinRoom);
        }

        [Fact]
        public async Task Close_when_empty_needs_an_open_room()
        {
            await using var lobby = new TestLobby();
            Assert.Contains("not possible right now", await new LifecycleActions(lobby.Runtime).CloseWhenEmptyAsync(true), StringComparison.Ordinal);
            Assert.Contains("not possible right now", await new LifecycleActions(lobby.Runtime).CloseAsync(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Before_the_bot_runs_every_action_says_so()
        {
            var actions = new LifecycleActions(new LobbyRuntime());
            Assert.Equal(LifecycleActions.NotRunning, await actions.CreateAsync("x"));
            Assert.Equal(LifecycleActions.NotRunning, await actions.AttachAsync("5"));
            Assert.Equal(LifecycleActions.NotRunning, await actions.CloseAsync());
            Assert.Equal(LifecycleActions.NotRunning, await actions.CloseWhenEmptyAsync(true));
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
                (await _renderer.RenderComponentAsync<LifecycleControls>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Snapshot"] = snapshot }))).ToHtmlString());

        [Fact]
        public async Task Idle_offers_create_and_attach_with_the_kept_title()
        {
            var html = await RenderAsync(Snapshot(LobbyStatus.Idle, keptTitle: "kept title"));
            Assert.Contains("Create lobby", html, StringComparison.Ordinal);
            Assert.Contains("Attach", html, StringComparison.Ordinal);
            Assert.Contains("placeholder=\"kept title\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Close lobby", html, StringComparison.Ordinal);
        }

        [Fact]
        public async Task An_open_room_offers_close_and_close_when_empty()
        {
            var html = await RenderAsync(Snapshot(LobbyStatus.Open, 7));
            Assert.Contains("Close lobby", html, StringComparison.Ordinal);
            Assert.Contains("Close when everyone has left", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Create lobby", html, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_room_bancho_has_not_made_yet_offers_cancel_rather_than_close()
        {
            var html = await RenderAsync(Snapshot(LobbyStatus.Creating));
            Assert.Contains(">Cancel<", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Close lobby", html, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_room_closing_when_empty_offers_to_keep_it_open()
        {
            var html = await RenderAsync(Snapshot(LobbyStatus.Open, 7, closing: true));
            Assert.Contains("Keep the lobby open", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Close when everyone has left", html, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_notice_about_the_room_is_shown_as_text()
        {
            var html = await RenderAsync(Snapshot(LobbyStatus.Idle) with { Notice = "Could not attach to room 42 (Bancho: <No such channel>)." });
            Assert.Contains("<p class=\"notice\" role=\"status\">Could not attach to room 42 (Bancho: &lt;No such channel&gt;).</p>", html, StringComparison.Ordinal);
            Assert.DoesNotContain("class=\"notice\"", await RenderAsync(Snapshot(LobbyStatus.Idle)), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Closing_offers_nothing()
        {
            var html = await RenderAsync(Snapshot(LobbyStatus.Closing, 7));
            Assert.DoesNotContain("<button", html, StringComparison.Ordinal);
        }
    }
}
