using OsuRotate.Domain;

namespace OsuRotate.Domain.Tests;

// Ported from osu-ahr's LobbyKeeperTest. Its option tests checked config-file conversion, which moves
// to the settings layer (phase 4 and 5); the parsing behind it is tested here directly. The host-kick
// counterattack needs match history and is left for phase 4.
public class LobbyKeeperTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static KeepChange? Apply(RoomKeeping current, string message)
    {
        var command = ChatCommand.Parse(message)!;
        return KeepCommand.Apply(current, command.Name, command.Argument);
    }

    private static RoomKeeping After(RoomKeeping current, string message) => Apply(current, message)?.After ?? current;

    public class Slots
    {
        [Fact]
        public void A_player_past_the_size_means_the_room_grew()
        {
            var slots = new SlotKeeper(T0) { Size = 4 };
            Assert.False(slots.Join(1, T0));
            Assert.False(slots.Join(2, T0));
            Assert.False(slots.Join(3, T0));
            Assert.False(slots.Join(4, T0));
            Assert.True(slots.Join(5, T0));
        }

        [Fact]
        public void Joining_past_an_empty_slot_means_a_slot_is_locked()
        {
            var slots = new SlotKeeper(T0) { Size = 4 };
            Assert.False(slots.Join(1, T0));
            Assert.False(slots.Join(2, T0));
            Assert.True(slots.Join(4, T0));
        }

        [Fact]
        public void Leaving_frees_the_slot()
        {
            var slots = new SlotKeeper(T0) { Size = 4 };
            for (var slot = 1; slot <= 4; slot++)
            {
                Assert.False(slots.Join(slot, T0));
            }

            slots.Leave(2, T0);
            Assert.False(slots.Join(2, T0));

            for (var slot = 1; slot <= 4; slot++)
            {
                slots.Leave(slot, T0);
            }

            Assert.False(slots.Join(1, T0));
        }

        [Fact]
        public void Moving_past_the_size_means_the_room_grew()
        {
            var slots = new SlotKeeper(T0) { Size = 4 };
            Assert.False(slots.Join(1, T0));
            Assert.False(slots.Join(2, T0));
            Assert.False(slots.Move(1, 4, T0));
            Assert.False(slots.Join(1, T0));
            Assert.True(slots.Move(1, 5, T0));
        }

        [Fact]
        public void A_slot_empty_for_ten_minutes_counts_as_locked()
        {
            var slots = new SlotKeeper(T0) { Size = 4 };
            slots.Join(1, T0);
            slots.Join(2, T0);
            Assert.False(slots.HasUnusedSlot(T0));
            Assert.False(slots.HasUnusedSlot(T0 + SlotKeeper.TimeToConsiderLocked / 2));
            Assert.False(slots.HasUnusedSlot(T0 + SlotKeeper.TimeToConsiderLocked - TimeSpan.FromMilliseconds(1)));
            Assert.True(slots.HasUnusedSlot(T0 + SlotKeeper.TimeToConsiderLocked));
        }

        [Fact]
        public void A_full_room_has_no_unused_slot()
        {
            var slots = new SlotKeeper(T0) { Size = 2 };
            slots.Join(1, T0);
            slots.Join(2, T0);
            Assert.False(slots.HasUnusedSlot(T0 + SlotKeeper.TimeToConsiderLocked * 2));
        }

        [Fact]
        public void A_move_empties_the_slot_left_behind()
        {
            var slots = new SlotKeeper(T0) { Size = 3 };
            slots.Join(1, T0);
            slots.Join(2, T0);
            Assert.False(slots.Move(2, 3, T0));
            Assert.True(slots.HasUnusedSlot(T0 + SlotKeeper.TimeToConsiderLocked));
        }

        [Fact]
        public void Reset_restarts_the_empty_slot_clock()
        {
            var slots = new SlotKeeper(T0) { Size = 4 };
            slots.Join(1, T0);
            slots.Reset(T0 + TimeSpan.FromMinutes(9));
            Assert.False(slots.HasUnusedSlot(T0 + TimeSpan.FromMinutes(11)));
            Assert.True(slots.HasUnusedSlot(T0 + TimeSpan.FromMinutes(19)));
        }

        [Fact]
        public void Room_state_past_the_size_means_the_room_grew()
        {
            var slots = new SlotKeeper(T0) { Size = 4 };
            Assert.False(slots.Apply([1, 2, 4], T0));
            Assert.True(slots.Apply([1, 5], T0));
        }

        [Fact]
        public void Room_state_replaces_what_was_known()
        {
            var slots = new SlotKeeper(T0) { Size = 2 };
            slots.Join(1, T0);
            slots.Join(2, T0);
            slots.Apply([1], T0);
            Assert.True(slots.HasUnusedSlot(T0 + SlotKeeper.TimeToConsiderLocked));
        }

        [Fact]
        public void Size_zero_never_reports_drift()
        {
            var slots = new SlotKeeper(T0);
            Assert.False(slots.Join(1, T0));
            Assert.False(slots.Join(2, T0));
            Assert.False(slots.Join(4, T0));
            Assert.False(slots.Join(5, T0));
            Assert.False(slots.Move(1, 9, T0));
            Assert.False(slots.Apply([1, 16], T0));
            Assert.False(slots.HasUnusedSlot(T0 + SlotKeeper.TimeToConsiderLocked * 2));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(17)]
        [InlineData(-3)]
        public void Slots_outside_the_room_are_ignored(int slot)
        {
            var slots = new SlotKeeper(T0) { Size = 4 };
            Assert.False(slots.Join(slot, T0));
            Assert.False(slots.Move(slot, slot, T0));
            slots.Leave(slot, T0);
            Assert.False(slots.Apply([slot], T0));
        }
    }

    public class Parsing
    {
        [Theory]
        [InlineData("1 1", TeamMode.TagCoop, ScoreMode.Accuracy)]
        [InlineData("0 3", TeamMode.HeadToHead, ScoreMode.ScoreV2)]
        [InlineData("TagTeamVs ScoreV2", TeamMode.TagTeamVs, ScoreMode.ScoreV2)]
        [InlineData("Head To Head, Score V2", TeamMode.HeadToHead, ScoreMode.ScoreV2)]
        [InlineData("head to head, accuracy", TeamMode.HeadToHead, ScoreMode.Accuracy)]
        [InlineData("tagcoop,scorev2", TeamMode.TagCoop, ScoreMode.ScoreV2)]
        [InlineData("Head To Head", TeamMode.HeadToHead, ScoreMode.Score)]
        [InlineData("tag coop", TeamMode.TagCoop, ScoreMode.Score)]
        [InlineData("team-vs", TeamMode.TeamVs, ScoreMode.Score)]
        [InlineData("ScoreV2", TeamMode.HeadToHead, ScoreMode.ScoreV2)]
        [InlineData("combo", TeamMode.HeadToHead, ScoreMode.Combo)]
        public void Mode(string text, TeamMode team, ScoreMode score)
        {
            Assert.Equal(new KeptMode(team, score), KeepCommand.TryParseMode(text, null));
        }

        [Theory]
        [InlineData("test")]
        [InlineData("aaaaa")]
        [InlineData("100 400 500")]
        [InlineData("112")]
        [InlineData("4")]
        [InlineData("0, 9")]
        public void Invalid_mode(string text)
        {
            Assert.Null(KeepCommand.TryParseMode(text, null));
        }

        [Fact]
        public void A_mode_given_alone_keeps_the_other_one()
        {
            var current = new KeptMode(TeamMode.TeamVs, ScoreMode.Accuracy);
            Assert.Equal(new KeptMode(TeamMode.TagCoop, ScoreMode.Accuracy), KeepCommand.TryParseMode("1", current));
            Assert.Equal(new KeptMode(TeamMode.TeamVs, ScoreMode.Combo), KeepCommand.TryParseMode("combo", current));
        }

        [Theory]
        [InlineData("None", Mods.None)]
        [InlineData("", Mods.None)]
        [InlineData("freemod", Mods.Freemod)]
        [InlineData("HD", Mods.Hidden)]
        [InlineData("HD, Dt", Mods.Hidden | Mods.DoubleTime)]
        [InlineData("Hidden, DoubleTime", Mods.Hidden | Mods.DoubleTime)]
        [InlineData("HD   Dt", Mods.Hidden | Mods.DoubleTime)]
        [InlineData("Freemod doubleTime Hidden", Mods.Freemod | Mods.DoubleTime)]
        [InlineData("Freemod relax relax2", Mods.Freemod)]
        [InlineData("relax relax2", Mods.Relax)]
        [InlineData("[hd, hr]", Mods.Hidden | Mods.HardRock)]
        [InlineData("Freemod HardRock Relax Nightcore", Mods.Freemod | Mods.DoubleTime | Mods.Nightcore)]
        [InlineData("nc", Mods.DoubleTime | Mods.Nightcore)]
        [InlineData("ez hr", Mods.Easy)]
        [InlineData("nf sd rx ap", Mods.NoFail)]
        [InlineData("ht dt nc", Mods.HalfTime)]
        [InlineData("sd rx", Mods.SuddenDeath)]
        [InlineData("rx ap", Mods.Relax)]
        [InlineData("co-op 4k mr", Mods.KeyCoop | Mods.Key4 | Mods.Mirror)]
        [InlineData("double", Mods.DoubleTime)]
        [InlineData("autopilot", Mods.Relax2)]
        [InlineData("aaaaaa", Mods.None)]
        [InlineData("aaaaaa, bbbbbb, sdsfs", Mods.None)]
        [InlineData("aaaaaa bbbbbb sdsfs hidden sdf", Mods.Hidden)]
        [InlineData("adfs 23fsd Relax xxxxxfds", Mods.Relax)]
        public void Mods_(string text, Mods expected)
        {
            Assert.Equal(expected, RoomModes.ParseMods(text));
        }

        [Fact]
        public void Mods_read_back_as_bancho_prints_them()
        {
            var mods = RoomModes.ParseMods("Freemod DoubleTime Nightcore");
            Assert.Equal("Freemod dt nc", mods.ToMpValue());
            Assert.Equal("Freemod, DoubleTime, Nightcore", mods.ToNames());
            Assert.Equal(mods, RoomModes.ParseMods(mods.ToNames()));
            Assert.Equal("None", Mods.None.ToMpValue());
            Assert.Equal("None", Mods.None.ToNames());
        }
    }

    public class OwnerCommands
    {
        private static readonly RoomKeeping Kept = new()
        {
            Mode = new KeptMode(TeamMode.HeadToHead, ScoreMode.Score),
            Size = 8,
            Mods = Mods.Hidden,
            Password = "aaaaaa",
            Title = "aaaaaa",
        };

        [Theory]
        [InlineData("*keep mode 0 1", TeamMode.HeadToHead, ScoreMode.Accuracy)]
        [InlineData("*keep mode 1", TeamMode.TagCoop, ScoreMode.Score)]
        [InlineData("*keep mode combo", TeamMode.HeadToHead, ScoreMode.Combo)]
        [InlineData("*keep mode head to head, accuracy", TeamMode.HeadToHead, ScoreMode.Accuracy)]
        [InlineData("*keep mode tag coop", TeamMode.TagCoop, ScoreMode.Score)]
        public void Keep_mode(string message, TeamMode team, ScoreMode score)
        {
            Assert.Equal(new KeptMode(team, score), After(Kept, message).Mode);
        }

        [Fact]
        public void Keep_mode_with_one_value_keeps_the_other()
        {
            var current = Kept with { Mode = new KeptMode(TeamMode.HeadToHead, ScoreMode.Accuracy) };
            Assert.Equal(new KeptMode(TeamMode.TagCoop, ScoreMode.Accuracy), After(current, "*keep mode 1").Mode);
        }

        [Theory]
        [InlineData("*keep mode null")]
        [InlineData("*no keep mode")]
        public void Stop_keeping_mode(string message)
        {
            Assert.Null(After(Kept, message).Mode);
        }

        [Theory]
        [InlineData("*keep mode")]
        [InlineData("*keep mode aaaaa")]
        [InlineData("*keep mode 100 400 500")]
        [InlineData("*keep mode 112")]
        [InlineData("*keepheadtohead")]
        [InlineData("*keep modes 1")]
        public void Invalid_keep_mode(string message)
        {
            Assert.Null(Apply(Kept, message));
        }

        [Theory]
        [InlineData("*keep size 0", 0)]
        [InlineData("*keep size 1", 1)]
        [InlineData("*keep size 16", 16)]
        [InlineData("*no keep size", 0)]
        public void Keep_size(string message, int expected)
        {
            Assert.Equal(expected, After(Kept, message).Size);
        }

        [Theory]
        [InlineData("*keep size")]
        [InlineData("*keep size aaaa")]
        [InlineData("*keep size12213 1")]
        [InlineData("*keep size -1")]
        [InlineData("*keep size -100000")]
        [InlineData("*keep size NaN")]
        [InlineData("*keep size 17")]
        [InlineData("*keep size 1000")]
        [InlineData("*keep sizesdfc")]
        [InlineData("*keep size12")]
        public void Invalid_keep_size(string message)
        {
            Assert.Null(Apply(Kept, message));
        }

        [Theory]
        [InlineData("*keep mod hidden", Mods.Hidden)]
        [InlineData("*keep mods hidden", Mods.Hidden)]
        [InlineData("*keep mods HD", Mods.Hidden)]
        [InlineData("*keep mods HIDDEN", Mods.Hidden)]
        [InlineData("*keep mods None", Mods.None)]
        [InlineData("*keep mods Freemod", Mods.Freemod)]
        [InlineData("*keep mods freemod hidden double", Mods.Freemod | Mods.DoubleTime)]
        public void Keep_mods(string message, Mods expected)
        {
            Assert.Equal(expected, After(Kept with { Mods = Mods.Flashlight }, message).Mods);
        }

        [Theory]
        [InlineData("*no keep mod")]
        [InlineData("*no keep mods")]
        [InlineData("*keep mod null")]
        public void Stop_keeping_mods(string message)
        {
            Assert.Null(After(Kept, message).Mods);
        }

        [Theory]
        [InlineData("*keep mods")]
        [InlineData("*keep mod")]
        public void Invalid_keep_mods(string message)
        {
            Assert.Null(Apply(Kept, message));
        }

        [Theory]
        [InlineData("*keep password testtest", "testtest")]
        [InlineData("*keep password test test", "test test")]
        [InlineData("*keep password t e s t", "t e s t")]
        [InlineData("*keep password     t e s t    ", "t e s t")]
        [InlineData("*keep password", "")]
        [InlineData("*keep password   ", "")]
        public void Keep_password(string message, string expected)
        {
            Assert.Equal(expected, After(Kept, message).Password);
        }

        [Fact]
        public void Stop_keeping_password()
        {
            Assert.Null(After(Kept, "*no keep password").Password);
        }

        [Theory]
        [InlineData("*keep title testtest", "testtest")]
        [InlineData("*keep name testtest", "testtest")]
        [InlineData("*keep title test test", "test test")]
        [InlineData("*keep title t e s t", "t e s t")]
        [InlineData("*keep title     t e s t    ", "t e s t")]
        public void Keep_title(string message, string expected)
        {
            Assert.Equal(expected, After(Kept, message).Title);
        }

        [Fact]
        public void A_title_is_cut_to_fifty_characters()
        {
            Assert.Equal(new string('x', 50), After(Kept, "*keep title " + new string('x', 60)).Title);
        }

        // osu-ahr kept an empty title, which Bancho cannot set.
        [Theory]
        [InlineData("*keep title")]
        [InlineData("*keep title   ")]
        [InlineData("*keep titles x")]
        public void Invalid_keep_title(string message)
        {
            Assert.Null(Apply(Kept, message));
        }

        [Theory]
        [InlineData("*no keep title")]
        [InlineData("*no keep name")]
        public void Stop_keeping_title(string message)
        {
            Assert.Null(After(Kept, message).Title);
        }

        [Theory]
        [InlineData("*no keep mode")]
        [InlineData("*no keep size")]
        [InlineData("*no keep mods")]
        [InlineData("*no keep password")]
        [InlineData("*no keep title")]
        [InlineData("*no keep")]
        [InlineData("*no keep everything")]
        public void Stopping_what_is_not_kept_changes_nothing(string message)
        {
            Assert.Null(Apply(new RoomKeeping(), message));
        }

        [Theory]
        [InlineData("*keep")]
        [InlineData("*keep everything")]
        [InlineData("*regulation keep mode 1")]
        public void Other_commands_change_nothing(string message)
        {
            Assert.Null(Apply(Kept, message));
        }

        [Fact]
        public void The_description_lists_what_is_kept_but_never_the_password()
        {
            Assert.Equal("Mode: HeadToHead, Score, Size: 8, Password: set, Mod(s): Hidden, Title: aaaaaa", Kept.Describe());
            Assert.Equal("Password: none", new RoomKeeping { Password = string.Empty }.Describe());
            Assert.Equal(string.Empty, new RoomKeeping().Describe());
        }
    }

    public class InTheLobby
    {
        private static LobbyHarness Create(RoomKeeping keeping, params string[] players)
        {
            var lobby = new LobbyHarness(s => s with { Keeping = keeping, HostAfkCheckInterval = TimeSpan.Zero, HostKickTolerance = 0 });
            lobby.AddPlayers(players.Length == 0 ? ["p1", "p2", "p3"] : players);
            lobby.Sent.Clear();
            return lobby;
        }

        private static readonly RoomKeeping Everything = new()
        {
            Mode = new KeptMode(TeamMode.TeamVs, ScoreMode.ScoreV2),
            Size = 8,
            Mods = Mods.Freemod | Mods.DoubleTime,
            Password = "secret",
            Title = "auto host rotation",
        };

        [Fact]
        public void Entering_the_room_sets_everything_kept()
        {
            var lobby = Create(Everything);
            lobby.Feed(new RoomCreated(1, "room"));
            Assert.Equal(
                [new SetRoomName("auto host rotation"), new SetTeamAndScoreMode(2, 3, 8), new SetMods("Freemod dt"), new SetPassword("secret")],
                lobby.Sent);
        }

        [Fact]
        public void Joining_the_room_does_the_same()
        {
            var lobby = Create(Everything);
            lobby.Feed(new RoomJoined(1));
            Assert.Equal(4, lobby.Sent.Count(c => c is not RequestReferees));
        }

        [Fact]
        public void Nothing_kept_sends_nothing()
        {
            var lobby = Create(new RoomKeeping());
            lobby.Feed(new RoomJoined(1));
            lobby.FinishMatchWith();
            Assert.DoesNotContain(lobby.Sent, c => c is SetRoomName or SetTeamAndScoreMode or SetSize or SetMods or SetPassword or RequestRoomState);
        }

        [Fact]
        public void Size_alone_uses_mp_size_and_an_empty_password_opens_the_room()
        {
            var lobby = Create(new RoomKeeping { Size = 4, Password = string.Empty });
            lobby.Feed(new RoomCreated(1, "room"));
            Assert.Equal([new SetSize(4), new SetPassword(null)], lobby.Sent);
        }

        [Fact]
        public void Mode_without_size_leaves_the_size_alone()
        {
            var lobby = Create(new RoomKeeping { Mode = new KeptMode(TeamMode.TagCoop, ScoreMode.Combo) });
            lobby.Feed(new RoomCreated(1, "room"));
            Assert.Equal([new SetTeamAndScoreMode(1, 2)], lobby.Sent);
        }

        [Fact]
        public void After_a_match_the_password_is_set_again_and_the_settings_are_checked()
        {
            var lobby = Create(Everything);
            lobby.FinishMatchWith();
            Assert.Contains(new SetPassword("secret"), lobby.Sent);
            Assert.Contains(lobby.Sent, c => c is RequestRoomState);
        }

        [Fact]
        public void Drifted_settings_are_put_back()
        {
            var lobby = Create(Everything);
            lobby.FinishMatchWith();
            Assert.Contains(new SetTeamAndScoreMode(2, 3, 8), lobby.Sent);
            Assert.Contains(new SetMods("Freemod dt"), lobby.Sent);
            Assert.Contains(new SetRoomName("auto host rotation"), lobby.Sent);
            Assert.Equal("TeamVs", lobby.TeamModeText);
            Assert.Equal("ScoreV2", lobby.WinCondition);
            Assert.Equal("Freemod, DoubleTime", lobby.ActiveMods);
            Assert.Equal("auto host rotation", lobby.RoomName);
        }

        [Fact]
        public void Settings_that_match_are_left_alone()
        {
            var lobby = Create(Everything);
            lobby.TeamModeText = "TeamVs";
            lobby.WinCondition = "ScoreV2";
            lobby.ActiveMods = "DoubleTime, Freemod";
            lobby.RoomName = "auto host rotation";
            lobby.FinishMatchWith();
            Assert.DoesNotContain(lobby.Sent, c => c is SetTeamAndScoreMode or SetMods or SetRoomName);
        }

        [Theory]
        [InlineData("HeadToHead", "ScoreV2")]
        [InlineData("TeamVs", "Score")]
        [InlineData("garbage", "ScoreV2")]
        [InlineData("TeamVs", "garbage")]
        public void Either_half_of_the_mode_drifting_puts_it_back(string team, string score)
        {
            var lobby = Create(new RoomKeeping { Mode = new KeptMode(TeamMode.TeamVs, ScoreMode.ScoreV2) });
            lobby.TeamModeText = team;
            lobby.WinCondition = score;
            lobby.FinishMatchWith();
            Assert.Contains(new SetTeamAndScoreMode(2, 3), lobby.Sent);
        }

        [Fact]
        public void A_room_that_grew_is_shrunk_on_join()
        {
            var lobby = Create(new RoomKeeping { Size = 4 }, "p1", "p2", "p3", "p4");
            lobby.Feed(new PlayerJoined("p5", 5, Team.None));
            Assert.Equal([new SetSize(4)], lobby.Sent);
        }

        [Fact]
        public void A_player_joining_past_a_locked_slot_resets_the_size()
        {
            var lobby = Create(new RoomKeeping { Size = 4 }, "p1");
            lobby.Feed(new PlayerJoined("p3", 3, Team.None));
            Assert.Equal([new SetSize(4)], lobby.Sent);
        }

        [Fact]
        public void A_player_who_left_frees_the_slot()
        {
            var lobby = Create(new RoomKeeping { Size = 2 }, "p1", "p2");
            lobby.RemovePlayer("p2");
            lobby.Time.Advance(SlotKeeper.TimeToConsiderLocked);
            lobby.FinishMatchWith();
            Assert.Contains(new SetSize(2), lobby.Sent);
        }

        [Fact]
        public void A_player_who_moved_frees_the_slot_they_left()
        {
            var lobby = Create(new RoomKeeping { Size = 3 }, "p1", "p2");
            lobby.Feed(new PlayerMoved("p2", 3));
            Assert.DoesNotContain(lobby.Sent, c => c is SetSize);
            lobby.Time.Advance(SlotKeeper.TimeToConsiderLocked);
            lobby.FinishMatchWith();
            Assert.Contains(new SetSize(3), lobby.Sent);
        }

        [Fact]
        public void Setting_the_size_restarts_the_ten_minutes()
        {
            var lobby = Create(new RoomKeeping { Size = 4 }, "p1", "p2");
            lobby.Time.Advance(SlotKeeper.TimeToConsiderLocked);
            lobby.FinishMatchWith();
            Assert.Equal(1, lobby.SentCount<SetSize>());

            lobby.Time.Advance(SlotKeeper.TimeToConsiderLocked / 2);
            lobby.FinishMatchWith();
            Assert.Equal(1, lobby.SentCount<SetSize>());

            lobby.Time.Advance(SlotKeeper.TimeToConsiderLocked / 2);
            lobby.FinishMatchWith();
            Assert.Equal(2, lobby.SentCount<SetSize>());
        }

        [Fact]
        public void A_full_room_keeps_its_size_after_a_match()
        {
            var lobby = Create(new RoomKeeping { Size = 2 }, "p1", "p2");
            lobby.Time.Advance(SlotKeeper.TimeToConsiderLocked);
            lobby.FinishMatchWith();
            Assert.DoesNotContain(lobby.Sent, c => c is SetSize);
        }

        [Fact]
        public void A_move_past_the_size_resets_it_and_frees_the_old_slot()
        {
            var lobby = Create(new RoomKeeping { Size = 4 }, "p1", "p2");
            lobby.Feed(new PlayerMoved("p2", 5));
            Assert.Equal([new SetSize(4)], lobby.Sent);

            lobby.Sent.Clear();
            lobby.Feed(new PlayerJoined("p3", 2, Team.None));
            Assert.DoesNotContain(lobby.Sent, c => c is SetSize);
        }

        [Fact]
        public void A_slot_left_empty_ten_minutes_resets_the_size_after_a_match()
        {
            var lobby = Create(new RoomKeeping { Size = 4 }, "p1", "p2");
            lobby.Time.Advance(SlotKeeper.TimeToConsiderLocked);
            lobby.FinishMatchWith();
            Assert.Contains(new SetSize(4), lobby.Sent);
        }

        [Fact]
        public void A_room_state_with_a_player_past_the_size_resets_it()
        {
            var lobby = Create(new RoomKeeping { Size = 2, Title = "room" }, "p1", "p2");
            lobby.Slots["p2"] = 3;
            lobby.FinishMatchWith();
            Assert.Contains(new SetSize(2), lobby.Sent);
        }

        [Fact]
        public void Owner_keep_command_applies_at_once()
        {
            var lobby = Create(new RoomKeeping());
            lobby.Chat(LobbyHarness.Owner, "*keep mode 2 3");
            Assert.Equal(new KeptMode(TeamMode.TeamVs, ScoreMode.ScoreV2), lobby.Engine.Settings.Keeping.Mode);
            Assert.Equal([new SetTeamAndScoreMode(2, 3)], lobby.Sent.Where(c => c is not Say));
            Assert.Contains("Keeping the lobby mode TeamVs, ScoreV2", lobby.Said);
        }

        [Theory]
        [InlineData("*keep size 6", "SetSize")]
        [InlineData("*keep mods hd", "SetMods")]
        [InlineData("*keep title hello", "SetRoomName")]
        [InlineData("*keep password pw", "SetPassword")]
        public void Each_keep_command_fixes_its_setting(string message, string command)
        {
            var lobby = Create(new RoomKeeping());
            lobby.Chat(LobbyHarness.Owner, message);
            Assert.Equal(command, Assert.Single(lobby.Sent, c => c is not Say).GetType().Name);
        }

        [Fact]
        public void The_password_is_never_said_in_the_lobby()
        {
            var lobby = Create(new RoomKeeping());
            lobby.Chat(LobbyHarness.Owner, "*keep password hunter2");
            Assert.Equal(["Keeping the lobby password."], lobby.Said);
            Assert.Equal("hunter2", lobby.Password);

            lobby.Chat(LobbyHarness.Owner, "*keep password");
            Assert.Contains("Keeping the lobby without a password.", lobby.Said);
            Assert.Null(lobby.Password);
        }

        [Fact]
        public void Stopping_a_kept_password_opens_the_room()
        {
            var lobby = Create(new RoomKeeping { Password = "pw" });
            lobby.Chat(LobbyHarness.Owner, "*no keep password");
            Assert.Null(lobby.Engine.Settings.Keeping.Password);
            Assert.Equal([new SetPassword(null)], lobby.Sent.Where(c => c is not Say));
            Assert.Contains("Disabled keeping the lobby password.", lobby.Said);
        }

        [Fact]
        public void Stopping_an_empty_kept_password_sends_nothing()
        {
            var lobby = Create(new RoomKeeping { Password = string.Empty });
            lobby.Chat(LobbyHarness.Owner, "*no keep password");
            Assert.Null(lobby.Engine.Settings.Keeping.Password);
            Assert.DoesNotContain(lobby.Sent, c => c is not Say);
        }

        [Fact]
        public void Stopping_keeps_the_room_as_it_is()
        {
            var lobby = Create(Everything);
            lobby.Chat(LobbyHarness.Owner, "*no keep mode");
            Assert.DoesNotContain(lobby.Sent, c => c is not Say);
            Assert.Contains("Disabled keeping the lobby team and score modes.", lobby.Said);
        }

        [Fact]
        public void Players_cannot_use_keep_commands()
        {
            var lobby = Create(new RoomKeeping());
            lobby.Chat("p1", "*keep mode 2 3");
            Assert.Null(lobby.Engine.Settings.Keeping.Mode);
            Assert.Empty(lobby.Sent);
        }

        [Fact]
        public void An_invalid_keep_command_is_ignored()
        {
            var lobby = Create(Everything);
            lobby.Chat(LobbyHarness.Owner, "*keep size 99");
            Assert.Equal(Everything, lobby.Engine.Settings.Keeping);
            Assert.Empty(lobby.Sent);
        }

        [Fact]
        public void No_regulation_still_reaches_the_map_checker()
        {
            var lobby = Create(new RoomKeeping());
            lobby.Chat(LobbyHarness.Owner, "*no regulation");
            Assert.False(lobby.Engine.Settings.Regulation.Enabled);
        }

        [Fact]
        public void Changing_the_size_takes_effect_for_slot_checks()
        {
            var lobby = Create(new RoomKeeping(), "p1", "p2");
            lobby.Chat(LobbyHarness.Owner, "*keep size 2");
            lobby.Sent.Clear();
            lobby.Feed(new PlayerJoined("p3", 3, Team.None));
            Assert.Equal([new SetSize(2)], lobby.Sent);
        }
    }
}
