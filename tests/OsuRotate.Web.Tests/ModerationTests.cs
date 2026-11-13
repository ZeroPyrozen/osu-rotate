using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OsuRotate.Application;
using OsuRotate.Domain;
using OsuRotate.Web.Bot;
using OsuRotate.Web.Components.Lobby;

namespace OsuRotate.Web.Tests;

// Phase 5, issue 05: chat and moderation (UC7).
public class ModerationTests
{
    private static LobbySnapshot Snapshot(LobbyStatus status = LobbyStatus.Open, string[]? denied = null, params string[] players) =>
        new(status, true, 7, "room", false,
            [.. (players.Length == 0 ? ["p1", "p 2"] : players).Select((n, i) => new PlayerSnapshot(n, null, i + 1, Team.None, MatchStatus.InLobby))],
            "p1", ["p1"], false, false, 0, denied ?? [], [], new LobbySettings { OwnerName = "creator" });

    public class Rules
    {
        [Fact]
        public void Chat_and_kicks_need_an_open_room_and_kicks_a_player_in_it()
        {
            Assert.True(ModerationActions.CanChat(Snapshot()));
            Assert.False(ModerationActions.CanChat(Snapshot(LobbyStatus.Rejoining)));
            Assert.False(ModerationActions.CanChat(null));

            Assert.True(ModerationActions.CanKick(Snapshot(), "p1"));
            Assert.True(ModerationActions.CanKick(Snapshot(), "p_2"));
            Assert.True(ModerationActions.CanKick(Snapshot(), "P1"));
            Assert.False(ModerationActions.CanKick(Snapshot(), "stranger"));
            Assert.False(ModerationActions.CanKick(Snapshot(LobbyStatus.Reconnecting), "p1"));
        }
    }

    public class Actions
    {
        [Fact]
        public async Task Chat_goes_out_at_the_owners_priority_and_shows_in_the_log()
        {
            await using var lobby = new TestLobby();
            await lobby.OpenAsync("p1");

            Assert.Null(await new ModerationActions(lobby.Runtime).SayAsync("  hello all  "));
            await TestLobby.WaitUntilAsync(() => lobby.Transport.Sent.Contains(new Say("hello all", CommandPriority.Admin)));
            Assert.Contains(lobby.Runtime.Chat, l => l is { Text: "hello all", Source: ChatSource.Owner });
        }

        [Theory]
        [InlineData(null, "Type something")]
        [InlineData("   ", "Type something")]
        public async Task Empty_chat_is_refused(string? text, string expected)
        {
            await using var lobby = new TestLobby();
            await lobby.OpenAsync("p1");
            Assert.Contains(expected, await new ModerationActions(lobby.Runtime).SayAsync(text), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Chat_longer_than_the_limit_is_refused_but_the_limit_itself_is_fine()
        {
            await using var lobby = new TestLobby();
            await lobby.OpenAsync("p1");
            var actions = new ModerationActions(lobby.Runtime);

            Assert.Contains("400 characters", await actions.SayAsync(new string('x', ModerationActions.MaxChatLength + 1)), StringComparison.Ordinal);
            Assert.Null(await actions.SayAsync(new string('x', ModerationActions.MaxChatLength)));
        }

        [Fact]
        public async Task Chat_with_no_room_is_refused()
        {
            await using var lobby = new TestLobby();
            Assert.Contains("not possible right now", await new ModerationActions(lobby.Runtime).SayAsync("hi"), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Kick_sends_mp_kick_for_a_player_in_the_room()
        {
            await using var lobby = new TestLobby();
            await lobby.OpenAsync("p1", "p2");

            Assert.Null(await new ModerationActions(lobby.Runtime).KickAsync("p2"));
            await TestLobby.WaitUntilAsync(() => lobby.Transport.Sent.Contains(new KickPlayer("p2")));
            Assert.Contains("not possible right now", await new ModerationActions(lobby.Runtime).KickAsync("stranger"), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Deny_and_allow_change_the_list()
        {
            await using var lobby = new TestLobby();
            await lobby.OpenAsync("p1", "p2");
            var actions = new ModerationActions(lobby.Runtime);

            Assert.Null(await actions.DenyAsync("  p2 "));
            await TestLobby.WaitUntilAsync(() => lobby.Snapshot.Denied.Contains("p2"));
            Assert.DoesNotContain("p2", lobby.Snapshot.Queue);

            Assert.Null(await actions.AllowAsync("p2"));
            await TestLobby.WaitUntilAsync(() => !lobby.Snapshot.Denied.Contains("p2") && lobby.Snapshot.Queue.Contains("p2"));
        }

        [Fact]
        public async Task Someone_not_in_the_room_can_be_denied_but_a_blank_name_cannot()
        {
            await using var lobby = new TestLobby();
            var actions = new ModerationActions(lobby.Runtime);

            Assert.Null(await actions.DenyAsync("Some One"));
            await TestLobby.WaitUntilAsync(() => lobby.Snapshot.Denied.Contains("some_one"));
            Assert.Contains("username", await actions.DenyAsync(" "), StringComparison.Ordinal);
            Assert.Contains("not possible right now", await actions.AllowAsync("never denied"), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Before_the_bot_runs_every_action_says_so()
        {
            var actions = new ModerationActions(new LobbyRuntime());
            Assert.Equal(LifecycleActions.NotRunning, await actions.SayAsync("hi"));
            Assert.Equal(LifecycleActions.NotRunning, await actions.KickAsync("p1"));
            Assert.Equal(LifecycleActions.NotRunning, await actions.DenyAsync("p1"));
            Assert.Equal(LifecycleActions.NotRunning, await actions.AllowAsync("p1"));
        }
    }

    public sealed class Rendering : IAsyncDisposable
    {
        private readonly TestLobby _lobby = new();
        private readonly ServiceProvider _services;
        private readonly HtmlRenderer _renderer;

        public Rendering()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(_lobby.Runtime);
            _services = services.BuildServiceProvider();
            _renderer = new HtmlRenderer(_services, _services.GetRequiredService<ILoggerFactory>());
        }

        public async ValueTask DisposeAsync()
        {
            await _renderer.DisposeAsync();
            await _services.DisposeAsync();
            await _lobby.DisposeAsync();
        }

        private Task<HtmlRootComponent> RenderAsync<T>(LobbySnapshot? snapshot)
            where T : IComponent =>
            _renderer.Dispatcher.InvokeAsync(() =>
                _renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Snapshot"] = snapshot })));

        private string Html(HtmlRootComponent root) => _renderer.Dispatcher.InvokeAsync(root.ToHtmlString).Result;

        [Fact]
        public async Task Chat_shows_live_and_as_text()
        {
            await _lobby.OpenAsync("p1");
            var root = await RenderAsync<ChatPanel>(Snapshot());
            Assert.Contains("No chat yet.", Html(root), StringComparison.Ordinal);

            _lobby.Transport.Emit(new ChatReceived("p1", "<script>alert(1)</script>", false));
            _lobby.Transport.Emit(new ChatReceived("p1", "dances", true));
            await TestLobby.WaitUntilAsync(() => Html(root).Contains("dances", StringComparison.Ordinal));

            var html = Html(root);
            Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
            Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
            Assert.Contains("* p1 dances", html, StringComparison.Ordinal);
            Assert.Contains("Send", html, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Chat_from_before_the_page_opened_is_shown_and_the_bot_is_marked()
        {
            await _lobby.OpenAsync("p1");
            _lobby.Transport.Emit(new ChatReceived("p1", "!q", false));
            await TestLobby.WaitUntilAsync(() => _lobby.Runtime.Chat.Any(l => l.Source == ChatSource.Bot));

            var html = Html(await RenderAsync<ChatPanel>(Snapshot()));
            Assert.Contains("p1:", html, StringComparison.Ordinal);
            Assert.Contains("creator (bot):", html, StringComparison.Ordinal);
            Assert.Contains("class=\"bot\"", html, StringComparison.Ordinal);
        }

        [Fact]
        public async Task With_no_room_there_is_no_send_box()
        {
            var html = Html(await RenderAsync<ChatPanel>(Snapshot(LobbyStatus.Idle)));
            Assert.DoesNotContain("Send", html, StringComparison.Ordinal);
        }

        [Fact]
        public async Task The_never_host_list_shows_everyone_on_it()
        {
            var html = Html(await RenderAsync<ModerationPanel>(Snapshot(denied: ["p_2", "far_away"])));

            Assert.Contains("far_away", html, StringComparison.Ordinal);
            Assert.Equal(2, html.Split("Allow again").Length - 1);

            // Kicking and denying a player in the room are on that player's row in the player list.
            Assert.DoesNotContain(">Kick<", html, StringComparison.Ordinal);
        }

        [Fact]
        public async Task An_empty_deny_list_says_so_and_names_can_still_be_added()
        {
            var html = Html(await RenderAsync<ModerationPanel>(Snapshot(LobbyStatus.Idle)));
            Assert.Contains("Nobody.", html, StringComparison.Ordinal);
            Assert.Contains("Never give host to", html, StringComparison.Ordinal);
            Assert.DoesNotContain(">Kick<", html, StringComparison.Ordinal);
        }
    }
}
