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

// Phase 5, issue 06: editing the rules (UC8).
public class RuleEditingTests
{
    private static readonly LobbySettings Defaults = new() { OwnerName = "creator" };

    public class Form
    {
        [Fact]
        public void An_untouched_form_gives_back_the_same_rules()
        {
            var settings = Defaults with
            {
                Keeping = new RoomKeeping { Mode = new KeptMode(TeamMode.TeamVs, ScoreMode.Accuracy), Size = 8, Password = "pw", Mods = Mods.Hidden | Mods.HardRock, Title = "room" },
                AutoStartEnabled = true,
                InfoAnnouncementInterval = TimeSpan.FromMinutes(5),
                Regulation = Defaults.Regulation with { StarMin = 4.5, StarMax = 6.25, LengthMaxSeconds = 300, Mode = GameMode.Mania, AllowConvert = false },
            };
            Assert.Equal(settings, RuleForm.From(settings).ToSettings(settings).Settings);
            Assert.Equal(Defaults, RuleForm.From(Defaults).ToSettings(Defaults).Settings);
        }

        [Fact]
        public void Every_field_reaches_the_rules()
        {
            var form = RuleForm.From(Defaults);
            form.RegulationEnabled = false;
            form.StarMin = 1;
            form.StarMax = 2;
            form.LengthMinSeconds = 30;
            form.LengthMaxSeconds = 90;
            form.Mode = GameMode.Taiko;
            form.AllowConvert = false;
            form.ViolationsAllowed = 1;
            form.KeepMode = true;
            form.Team = TeamMode.TagCoop;
            form.Score = ScoreMode.Combo;
            form.Size = 4;
            form.KeepMods = true;
            form.ModsText = "freemod dt";
            form.Title = "  new title ";
            form.SkipShare = 0.1;
            form.SkipMinimum = 3;
            form.StartShare = 0.2;
            form.StartMinimum = 4;
            form.StartWhenAllReady = false;
            form.AbortShare = 0.3;
            form.AbortMinimum = 5;
            form.SkipAfkHost = false;
            form.HostAfkFirstCheckSeconds = 11;
            form.HostAfkCheckIntervalSeconds = 12;
            form.AfkKickEnabled = false;
            form.AfkKickThreshold = 7;
            form.AfkKickCooldownSeconds = 13;
            form.AutoStartEnabled = true;
            form.AutoStartClearsHost = true;
            form.AutoStartDelaySeconds = 20;
            form.AbortStuckMatch = false;
            form.StuckMatchFinishedShare = 0.9;
            form.StuckMatchDelaySeconds = 14;
            form.HostKickTolerance = 2;
            form.HostKickCheckIntervalSeconds = 15;
            form.InfoMessage = "one\r\ntwo\r\n";
            form.InfoAnnouncementIntervalSeconds = 600;

            var expected = Defaults with
            {
                Regulation = Defaults.Regulation with { Enabled = false, StarMin = 1, StarMax = 2, LengthMinSeconds = 30, LengthMaxSeconds = 90, Mode = GameMode.Taiko, AllowConvert = false, ViolationsAllowed = 1 },
                Keeping = new RoomKeeping { Mode = new KeptMode(TeamMode.TagCoop, ScoreMode.Combo), Size = 4, Mods = Mods.Freemod | Mods.DoubleTime, Title = "new title" },
                SkipVote = new VoteRule(0.1, 3),
                StartVote = new VoteRule(0.2, 4),
                StartWhenAllReady = false,
                AbortVote = new VoteRule(0.3, 5),
                SkipAfkHost = false,
                HostAfkFirstCheck = TimeSpan.FromSeconds(11),
                HostAfkCheckInterval = TimeSpan.FromSeconds(12),
                AfkKickEnabled = false,
                AfkKickThreshold = 7,
                AfkKickCooldown = TimeSpan.FromSeconds(13),
                AutoStartEnabled = true,
                AutoStartClearsHost = true,
                AutoStartDelay = TimeSpan.FromSeconds(20),
                AbortStuckMatch = false,
                StuckMatchFinishedShare = 0.9,
                StuckMatchDelay = TimeSpan.FromSeconds(14),
                HostKickTolerance = 2,
                HostKickCheckInterval = TimeSpan.FromSeconds(15),
                InfoMessage = "one\ntwo",
                InfoAnnouncementInterval = TimeSpan.FromMinutes(10),
            };
            Assert.Equal(expected, form.ToSettings(Defaults).Settings);
        }

        [Fact]
        public void Switches_off_mean_not_kept()
        {
            var current = Defaults with { Keeping = new RoomKeeping { Mode = new KeptMode(TeamMode.TeamVs, ScoreMode.Score), Password = "pw", Mods = Mods.Hidden, Title = "t" } };
            var form = RuleForm.From(current);
            form.KeepMode = false;
            form.Password = PasswordKeeping.NotKept;
            form.KeepMods = false;
            form.Title = " ";
            Assert.Equal(new RoomKeeping(), form.ToSettings(current).Settings!.Keeping);
        }

        [Fact]
        public void The_password_never_reaches_the_form_and_empty_keeps_the_old_one()
        {
            var current = Defaults with { Keeping = new RoomKeeping { Password = "secret" } };
            var form = RuleForm.From(current);
            Assert.Equal(PasswordKeeping.Set, form.Password);
            Assert.Null(form.NewPassword);
            Assert.Equal("secret", form.ToSettings(current).Settings!.Keeping.Password);

            form.NewPassword = "other";
            Assert.Equal("other", form.ToSettings(current).Settings!.Keeping.Password);
        }

        [Fact]
        public void A_new_password_must_be_entered_and_open_means_none()
        {
            var form = RuleForm.From(Defaults);
            form.Password = PasswordKeeping.Set;
            Assert.Contains("Enter the password", form.ToSettings(Defaults).Problem, StringComparison.Ordinal);

            var open = Defaults with { Keeping = new RoomKeeping { Password = string.Empty } };
            Assert.Equal(PasswordKeeping.Open, RuleForm.From(open).Password);
            Assert.Equal(string.Empty, RuleForm.From(open).ToSettings(open).Settings!.Keeping.Password);
        }

        [Theory]
        [InlineData("none", Mods.None)]
        [InlineData("", Mods.None)]
        [InlineData("HD, hr", Mods.Hidden | Mods.HardRock)]
        public void Mods_are_read_by_name(string text, Mods expected)
        {
            var form = RuleForm.From(Defaults);
            form.KeepMods = true;
            form.ModsText = text;
            Assert.Equal(expected, form.ToSettings(Defaults).Settings!.Keeping.Mods);
        }

        [Fact]
        public void Unknown_mods_are_refused()
        {
            var form = RuleForm.From(Defaults);
            form.KeepMods = true;
            form.ModsText = "banana";
            var (settings, problem) = form.ToSettings(Defaults);
            Assert.Null(settings);
            Assert.Contains("not recognised", problem, StringComparison.Ordinal);
        }
    }

    public class Actions
    {
        [Fact]
        public async Task Valid_rules_reach_the_lobby()
        {
            await using var lobby = new TestLobby();
            await lobby.OpenAsync("p1");
            var form = RuleForm.From(lobby.Snapshot.Settings);
            form.AfkKickThreshold = 9;
            form.Size = 8;

            Assert.Null(await new RuleActions(lobby.Runtime).SaveAsync(form));
            await TestLobby.WaitUntilAsync(() => lobby.Snapshot.Settings.AfkKickThreshold == 9);
            await TestLobby.WaitUntilAsync(() => lobby.Transport.Sent.Contains(new SetSize(8)));
        }

        [Fact]
        public async Task Invalid_rules_are_refused_with_every_reason()
        {
            await using var lobby = new TestLobby();
            var form = RuleForm.From(lobby.Snapshot.Settings);
            form.AfkKickThreshold = 0;
            form.Size = 17;

            var message = await new RuleActions(lobby.Runtime).SaveAsync(form);
            Assert.Contains("AFK kick threshold", message, StringComparison.Ordinal);
            Assert.Contains("kept size", message, StringComparison.Ordinal);
            await lobby.Actor.DrainAsync().WaitAsync(TestLobby.Timeout);
            Assert.Equal(6, lobby.Snapshot.Settings.AfkKickThreshold);
        }

        [Fact]
        public async Task A_form_that_cannot_be_read_says_why()
        {
            await using var lobby = new TestLobby();
            var form = RuleForm.From(lobby.Snapshot.Settings);
            form.Password = PasswordKeeping.Set;
            Assert.Contains("Enter the password", await new RuleActions(lobby.Runtime).SaveAsync(form), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Before_the_bot_runs_saving_says_so()
        {
            Assert.Equal(LifecycleActions.NotRunning, await new RuleActions(new LobbyRuntime()).SaveAsync(RuleForm.From(Defaults)));
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
            services.AddSingleton(new LobbyRuntime());
            _services = services.BuildServiceProvider();
            _renderer = new HtmlRenderer(_services, _services.GetRequiredService<ILoggerFactory>());
        }

        public async ValueTask DisposeAsync()
        {
            await _renderer.DisposeAsync();
            await _services.DisposeAsync();
        }

        private Task<string> RenderAsync(LobbySettings? settings) =>
            _renderer.Dispatcher.InvokeAsync(async () =>
            {
                var snapshot = settings is null
                    ? null
                    : new LobbySnapshot(LobbyStatus.Idle, false, null, null, false, [], null, [], false, false, 0, [], [], settings);
                var root = await _renderer.RenderComponentAsync<RulesPanel>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Snapshot"] = snapshot }));
                return root.ToHtmlString();
            });

        [Fact]
        public async Task The_form_shows_the_current_rules_but_never_the_password()
        {
            var html = await RenderAsync(Defaults with
            {
                Keeping = new RoomKeeping { Password = "hunter2", Title = "my room" },
                Regulation = Defaults.Regulation with { StarMax = 6.5 },
            });

            Assert.Contains("Save rules", html, StringComparison.Ordinal);
            Assert.Contains("value=\"6.5\"", html, StringComparison.Ordinal);
            Assert.Contains("value=\"my room\"", html, StringComparison.Ordinal);
            Assert.Contains("type=\"password\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("hunter2", html, StringComparison.Ordinal);
            Assert.Contains("placeholder=\"unchanged\"", html, StringComparison.Ordinal);
        }

        [Fact]
        public async Task With_no_lobby_there_is_no_form()
        {
            Assert.DoesNotContain("Save rules", await RenderAsync(null), StringComparison.Ordinal);
        }
    }
}
