using OsuRotate.Domain;

namespace OsuRotate.Domain.Tests;

// Phase 5, issue 06: the owner editing the rules from the dashboard (UC8).
public class SettingsEditTests
{
    private static readonly LobbySettings Defaults = new() { OwnerName = "creator" };

    public class Validation
    {
        [Fact]
        public void The_defaults_are_valid() => Assert.Empty(SettingsRules.Validate(Defaults));

        public static TheoryData<string, LobbySettings> Invalid => new()
        {
            { "star rating limits", Defaults with { Regulation = Defaults.Regulation with { StarMin = -0.1 } } },
            { "star rating limits", Defaults with { Regulation = Defaults.Regulation with { StarMax = SettingsRules.MaxStarRating + 0.01 } } },
            { "star rating limits", Defaults with { Regulation = Defaults.Regulation with { StarMin = double.NaN } } },
            { "star rating limits", Defaults with { Regulation = Defaults.Regulation with { StarMax = double.PositiveInfinity } } },
            { "highest star rating", Defaults with { Regulation = Defaults.Regulation with { StarMin = 5, StarMax = 4 } } },
            { "length in seconds limits", Defaults with { Regulation = Defaults.Regulation with { LengthMinSeconds = -1 } } },
            { "length in seconds limits", Defaults with { Regulation = Defaults.Regulation with { LengthMaxSeconds = SettingsRules.MaxLengthSeconds + 1 } } },
            { "highest length", Defaults with { Regulation = Defaults.Regulation with { LengthMinSeconds = 120, LengthMaxSeconds = 60 } } },
            { "game mode", Defaults with { Regulation = Defaults.Regulation with { Mode = (GameMode)4 } } },
            { "violations allowed", Defaults with { Regulation = Defaults.Regulation with { ViolationsAllowed = -1 } } },
            { "map description", Defaults with { Regulation = Defaults.Regulation with { DescriptionTemplate = " " } } },
            { "map description", Defaults with { Regulation = Defaults.Regulation with { DescriptionTemplate = "a\nb" } } },
            { "team and score", Defaults with { Keeping = new RoomKeeping { Mode = new KeptMode((TeamMode)4, ScoreMode.Score) } } },
            { "team and score", Defaults with { Keeping = new RoomKeeping { Mode = new KeptMode(TeamMode.TeamVs, (ScoreMode)4) } } },
            { "kept size", Defaults with { Keeping = new RoomKeeping { Size = -1 } } },
            { "kept size", Defaults with { Keeping = new RoomKeeping { Size = KeepCommand.MaxSize + 1 } } },
            { "kept mods", Defaults with { Keeping = new RoomKeeping { Mods = Mods.Easy | Mods.HardRock } } },
            { "kept password", Defaults with { Keeping = new RoomKeeping { Password = "two words" } } },
            { "kept password", Defaults with { Keeping = new RoomKeeping { Password = new string('x', SettingsRules.MaxPasswordLength + 1) } } },
            { "kept title", Defaults with { Keeping = new RoomKeeping { Title = " " } } },
            { "kept title", Defaults with { Keeping = new RoomKeeping { Title = new string('x', RoomKeeping.MaxTitleLength + 1) } } },
            { "kept title", Defaults with { Keeping = new RoomKeeping { Title = "a\rb" } } },
            { "skip vote", Defaults with { SkipVote = new VoteRule(1.01, 2) } },
            { "start vote", Defaults with { StartVote = new VoteRule(-0.01, 2) } },
            { "abort vote", Defaults with { AbortVote = new VoteRule(0.5, 0) } },
            { "abort vote", Defaults with { AbortVote = new VoteRule(double.NaN, 1) } },
            { "first host AFK check", Defaults with { HostAfkFirstCheck = TimeSpan.FromSeconds(-1) } },
            { "host AFK check interval", Defaults with { HostAfkCheckInterval = SettingsRules.MaximumDelay + TimeSpan.FromSeconds(1) } },
            { "AFK kick threshold", Defaults with { AfkKickThreshold = 0 } },
            { "AFK kick cooldown", Defaults with { AfkKickCooldown = SettingsRules.MinimumAfkKickCooldown - TimeSpan.FromSeconds(1) } },
            { "auto start delay", Defaults with { AutoStartDelay = LobbyEngine.MinimumAutoStartDelay - TimeSpan.FromSeconds(1) } },
            { "stuck match", Defaults with { StuckMatchFinishedShare = 1.01 } },
            { "stuck match delay", Defaults with { StuckMatchDelay = TimeSpan.FromSeconds(-1) } },
            { "host-kick tolerance", Defaults with { HostKickTolerance = -1 } },
            { "host-kick check interval", Defaults with { HostKickCheckInterval = SettingsRules.MinimumHostKickCheckInterval - TimeSpan.FromSeconds(1) } },
            { "info message must", Defaults with { InfoMessage = "  " } },
            { "info message must", Defaults with { InfoMessage = new string('x', SettingsRules.MaxInfoMessageLength + 1) } },
            { "Each line", Defaults with { InfoMessage = "short\n" + new string('x', SettingsRules.MaxChatLineLength + 1) } },
            { "info announcement interval", Defaults with { InfoAnnouncementInterval = LobbyEngine.MinimumInfoAnnouncementInterval - TimeSpan.FromSeconds(1) } },
        };

        [Theory]
        [MemberData(nameof(Invalid))]
        public void Invalid_values_are_refused_with_a_message(string expected, LobbySettings settings)
        {
            var problem = Assert.Single(SettingsRules.Validate(settings));
            Assert.Contains(expected, problem, StringComparison.Ordinal);
        }

        public static TheoryData<LobbySettings> Valid => new()
        {
            Defaults with { Regulation = Defaults.Regulation with { StarMin = 0, StarMax = SettingsRules.MaxStarRating, ViolationsAllowed = 0 } },
            Defaults with { Regulation = Defaults.Regulation with { StarMin = 4, StarMax = 4, LengthMinSeconds = 60, LengthMaxSeconds = 60 } },
            Defaults with { Regulation = Defaults.Regulation with { StarMin = 6, StarMax = 0, LengthMinSeconds = SettingsRules.MaxLengthSeconds } },
            Defaults with { Keeping = new RoomKeeping { Mode = new KeptMode(TeamMode.TagTeamVs, ScoreMode.ScoreV2), Size = KeepCommand.MaxSize, Mods = Mods.Freemod | Mods.DoubleTime, Password = "", Title = new string('x', RoomKeeping.MaxTitleLength) } },
            Defaults with { Keeping = new RoomKeeping { Password = new string('x', SettingsRules.MaxPasswordLength) } },
            Defaults with { SkipVote = new VoteRule(0, 1), StartVote = new VoteRule(1, 16) },
            Defaults with { HostAfkFirstCheck = TimeSpan.Zero, HostAfkCheckInterval = SettingsRules.MaximumDelay, AfkKickThreshold = 1, AfkKickCooldown = SettingsRules.MinimumAfkKickCooldown },
            Defaults with { AutoStartDelay = LobbyEngine.MinimumAutoStartDelay, StuckMatchFinishedShare = 1, StuckMatchDelay = TimeSpan.Zero, HostKickTolerance = 0 },
            Defaults with { HostKickCheckInterval = SettingsRules.MinimumHostKickCheckInterval, InfoAnnouncementInterval = LobbyEngine.MinimumInfoAnnouncementInterval },
            Defaults with { InfoMessage = new string('x', SettingsRules.MaxChatLineLength) + "\n" + new string('y', SettingsRules.MaxChatLineLength) },
        };

        [Theory]
        [MemberData(nameof(Valid))]
        public void Values_at_the_limits_are_accepted(LobbySettings settings) => Assert.Empty(SettingsRules.Validate(settings));

        [Fact]
        public void Every_problem_is_listed()
        {
            var settings = Defaults with { AfkKickThreshold = 0, HostKickTolerance = -1, InfoMessage = string.Empty };
            Assert.Equal(3, SettingsRules.Validate(settings).Count);
        }
    }

    public class Changes
    {
        private static LobbyHarness Create(Func<LobbySettings, LobbySettings>? configure = null)
        {
            var lobby = new LobbyHarness(s => (configure ?? (x => x))(s with { HostAfkCheckInterval = TimeSpan.Zero }));
            lobby.AddPlayers("p1", "p2");
            lobby.Sent.Clear();
            return lobby;
        }

        private static void Change(LobbyHarness lobby, Func<LobbySettings, LobbySettings> edit) =>
            lobby.Run(() => lobby.Engine.ChangeSettings(edit(lobby.Engine.Settings)));

        [Fact]
        public void A_valid_change_reaches_the_engine()
        {
            var lobby = Create();
            Change(lobby, s => s with { AfkKickThreshold = 9, SkipVote = new VoteRule(0.8, 3) });
            Assert.Equal(9, lobby.Engine.Settings.AfkKickThreshold);
            Assert.Equal(new VoteRule(0.8, 3), lobby.Engine.SkipVotes.Rule);
            Assert.Empty(lobby.Sent);
        }

        [Fact]
        public void An_invalid_change_changes_nothing()
        {
            var lobby = Create();
            var before = lobby.Engine.Settings;
            Change(lobby, s => s with { AfkKickThreshold = 0, Keeping = new RoomKeeping { Size = 8 } });
            Assert.Same(before, lobby.Engine.Settings);
            Assert.Empty(lobby.Sent);
        }

        [Fact]
        public void The_owner_and_version_cannot_be_changed()
        {
            var lobby = Create();
            Change(lobby, s => s with { OwnerName = "someone", BotVersion = "9.9.9", AfkKickThreshold = 2 });
            Assert.Equal(LobbyHarness.Owner, lobby.Engine.Settings.OwnerName);
            Assert.Equal(new LobbySettings { OwnerName = "x" }.BotVersion, lobby.Engine.Settings.BotVersion);
            Assert.Equal(2, lobby.Engine.Settings.AfkKickThreshold);
        }

        [Fact]
        public void A_new_kept_mode_and_size_are_set_at_once()
        {
            var lobby = Create();
            Change(lobby, s => s with { Keeping = new RoomKeeping { Mode = new KeptMode(TeamMode.TeamVs, ScoreMode.Accuracy), Size = 8 } });
            Assert.Equal([new SetTeamAndScoreMode((int)TeamMode.TeamVs, (int)ScoreMode.Accuracy, 8)], lobby.Sent);
        }

        [Fact]
        public void A_new_kept_size_alone_is_set()
        {
            var lobby = Create();
            Change(lobby, s => s with { Keeping = new RoomKeeping { Size = 8 } });
            Assert.Equal([new SetSize(8)], lobby.Sent);
        }

        [Fact]
        public void No_longer_keeping_the_mode_or_size_sends_nothing()
        {
            var lobby = Create(s => s with { Keeping = new RoomKeeping { Size = 8, Mods = Mods.Hidden, Title = "t" } });
            Change(lobby, s => s with { Keeping = new RoomKeeping() });
            Assert.Empty(lobby.Sent);
        }

        [Fact]
        public void New_kept_mods_and_title_are_set_at_once()
        {
            var lobby = Create();
            Change(lobby, s => s with { Keeping = new RoomKeeping { Mods = Mods.Hidden | Mods.HardRock, Title = "new title" } });
            Assert.Contains(new SetMods((Mods.Hidden | Mods.HardRock).ToMpValue()), lobby.Sent);
            Assert.Contains(new SetRoomName("new title"), lobby.Sent);
            Assert.Equal(2, lobby.Sent.Count);
        }

        [Fact]
        public void A_new_kept_password_is_set_and_dropping_it_opens_the_room()
        {
            var lobby = Create();
            Change(lobby, s => s with { Keeping = new RoomKeeping { Password = "secret" } });
            Assert.Equal([new SetPassword("secret")], lobby.Sent);

            lobby.Sent.Clear();
            Change(lobby, s => s with { Keeping = new RoomKeeping() });
            Assert.Equal([new SetPassword(null)], lobby.Sent);
        }

        [Fact]
        public void Keeping_the_room_open_clears_the_password()
        {
            var lobby = Create();
            Change(lobby, s => s with { Keeping = new RoomKeeping { Password = string.Empty } });
            Assert.Equal([new SetPassword(null)], lobby.Sent);

            lobby.Sent.Clear();
            Change(lobby, s => s with { Keeping = new RoomKeeping() });
            Assert.Empty(lobby.Sent);
        }

        [Fact]
        public void Unchanged_keeps_are_not_set_again()
        {
            var lobby = Create(s => s with { Keeping = new RoomKeeping { Size = 8, Mods = Mods.Hidden, Password = "pw", Title = "t" } });
            Change(lobby, s => s with { AfkKickThreshold = 3 });
            Assert.Empty(lobby.Sent);
        }

        [Fact]
        public void A_new_regulation_is_announced()
        {
            var lobby = Create();
            Change(lobby, s => s with { Regulation = s.Regulation with { StarMax = 5 } });
            Assert.Equal([$"New regulation: {lobby.Engine.Settings.Regulation.Describe()}"], lobby.Said);
        }

        [Fact]
        public void Turning_the_map_checker_off_is_announced_alone()
        {
            var lobby = Create();
            Change(lobby, s => s with { Regulation = s.Regulation with { Enabled = false } });
            Assert.Equal(["Map Checker plugin disabled."], lobby.Said);

            lobby.Sent.Clear();
            Change(lobby, s => s with { Regulation = s.Regulation with { Enabled = true } });
            Assert.Equal(["Map Checker plugin enabled."], lobby.Said);
        }

        [Fact]
        public void Violations_and_the_description_are_not_announced()
        {
            var lobby = Create();
            Change(lobby, s => s with { Regulation = s.Regulation with { ViolationsAllowed = 1, DescriptionTemplate = "${title}" } });
            Assert.Empty(lobby.Sent);
            Assert.Equal(1, lobby.Engine.Settings.Regulation.ViolationsAllowed);
        }

        [Fact]
        public void The_chat_command_still_announces_the_same_way()
        {
            var lobby = Create();
            lobby.Feed(new ChatReceived(LobbyHarness.Owner, "*regulation star_max=5", false));
            Assert.Contains($"New regulation: {lobby.Engine.Settings.Regulation.Describe()}", lobby.Said);
        }
    }
}
