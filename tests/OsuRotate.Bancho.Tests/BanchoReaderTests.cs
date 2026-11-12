using OsuRotate.Domain;
using OsuRotate.Infrastructure.Bancho;

namespace OsuRotate.Bancho.Tests;

public class BanchoReaderTests
{
    private const string Room53084403 = "Room name: 5* (´・ω・`) host rotate, History: https://osu.ppy.sh/mp/53084403";
    private const string Beatmap853167 = "Beatmap: https://osu.ppy.sh/b/853167 Silent Siren - Hachigatsu no Yoru [August]";
    private const string HeadToHead = "Team mode: HeadToHead, Win condition: Score";
    private const string Freemod = "Active mods: Freemod";

    private static RoomPlayer P(int slot, string name, int id, bool host = false, string options = "", string ready = "Not Ready", Team team = Team.None) =>
        new(slot, ready, id, name, host, team, options);

    private sealed record Case(string[] Lines, string Name, long RoomId, int BeatmapId, string BeatmapTitle, string TeamMode, string Mods, RoomPlayer[] Players);

    private static readonly Dictionary<string, Case> Cases = new()
    {
        ["host first"] = new(
            [Room53084403, Beatmap853167, HeadToHead, Freemod, "Players: 5",
             "Slot 1  Not Ready https://osu.ppy.sh/u/111 p1              [Host]",
             "Slot 2  Not Ready https://osu.ppy.sh/u/222 p2              [Hidden, DoubleTime]",
             "Slot 3  Not Ready https://osu.ppy.sh/u/333 p3              ",
             "Slot 4  Not Ready https://osu.ppy.sh/u/444 p4              ",
             "Slot 5  Not Ready https://osu.ppy.sh/u/555 p5              "],
            "5* (´・ω・`) host rotate", 53084403, 853167, "Silent Siren - Hachigatsu no Yoru [August]", "HeadToHead", "Freemod",
            [P(1, "p1", 111, true, "Host"), P(2, "p2", 222, options: "Hidden, DoubleTime"), P(3, "p3", 333), P(4, "p4", 444), P(5, "p5", 555)]),

        ["host in the middle"] = new(
            [Room53084403, Beatmap853167, HeadToHead, Freemod, "Players: 5",
             "Slot 1  Not Ready https://osu.ppy.sh/u/111 p1              ",
             "Slot 2  Not Ready https://osu.ppy.sh/u/222 p2              [Hidden, DoubleTime]",
             "Slot 3  Not Ready https://osu.ppy.sh/u/333 p3              [Host]",
             "Slot 4  Not Ready https://osu.ppy.sh/u/444 p4              ",
             "Slot 5  Not Ready https://osu.ppy.sh/u/555 p5              "],
            "5* (´・ω・`) host rotate", 53084403, 853167, "Silent Siren - Hachigatsu no Yoru [August]", "HeadToHead", "Freemod",
            [P(1, "p1", 111), P(2, "p2", 222, options: "Hidden, DoubleTime"), P(3, "p3", 333, true, "Host"), P(4, "p4", 444), P(5, "p5", 555)]),

        ["gaps in slots"] = new(
            [Room53084403, Beatmap853167, HeadToHead, Freemod, "Players: 5",
             "Slot 1  Not Ready https://osu.ppy.sh/u/666 p6              ",
             "Slot 2  Not Ready https://osu.ppy.sh/u/222 p2              [Hidden, DoubleTime]",
             "Slot 4  Not Ready https://osu.ppy.sh/u/444 p4              [Host]",
             "Slot 5  Not Ready https://osu.ppy.sh/u/555 p5              ",
             "Slot 6  Not Ready https://osu.ppy.sh/u/777 p7              "],
            "5* (´・ω・`) host rotate", 53084403, 853167, "Silent Siren - Hachigatsu no Yoru [August]", "HeadToHead", "Freemod",
            [P(1, "p6", 666), P(2, "p2", 222, options: "Hidden, DoubleTime"), P(4, "p4", 444, true, "Host"), P(5, "p5", 555), P(6, "p7", 777)]),

        ["host with mods and spaces in names"] = new(
            ["Room name: 4-5 auto host rotation, History: https://osu.ppy.sh/mp/54771533",
             "Beatmap: https://osu.ppy.sh/b/1188131 yuikonnu - Ghost Rule [Hyper]", HeadToHead, Freemod, "Players: 5",
             "Slot 1  Not Ready https://osu.ppy.sh/u/123 aaaaaa          [Hidden]",
             "Slot 3  Not Ready https://osu.ppy.sh/u/456 bbbbbb111       ",
             "Slot 4  Not Ready https://osu.ppy.sh/u/789 ccc 23          [Host / Hidden]",
             "Slot 6  Not Ready https://osu.ppy.sh/u/147 d               [Hidden]",
             "Slot 7  Not Ready https://osu.ppy.sh/u/258 eee             "],
            "4-5 auto host rotation", 54771533, 1188131, "yuikonnu - Ghost Rule [Hyper]", "HeadToHead", "Freemod",
            [P(1, "aaaaaa", 123, options: "Hidden"), P(3, "bbbbbb111", 456), P(4, "ccc 23", 789, true, "Host / Hidden"), P(6, "d", 147, options: "Hidden"), P(7, "eee", 258)]),

        ["brackets and 15 character names"] = new(
            ["Room name: 4-5* auto host rotaion, History: https://osu.ppy.sh/mp/54581109",
             "Beatmap: https://osu.ppy.sh/b/1418503 tofubeats - CANDYYYLAND feat LIZ - Pa's Lam System Remix [Nathan's Extra]", HeadToHead, Freemod, "Players: 7",
             "Slot 1  Not Ready https://osu.ppy.sh/u/1 player1         [Host / Hidden, HardRock]",
             "Slot 2  Not Ready https://osu.ppy.sh/u/2 player2[abc]    ",
             "Slot 3  Not Ready https://osu.ppy.sh/u/3 pl a ye r3      ",
             "Slot 4  Not Ready https://osu.ppy.sh/u/4 pl_a[y]er4      ",
             "Slot 5  Not Ready https://osu.ppy.sh/u/5 player5abcdefga ",
             "Slot 6  Not Ready https://osu.ppy.sh/u/6 pla y e r 6efga [Hidden]",
             "Slot 16  Not Ready https://osu.ppy.sh/u/7 player7abc[fga] [Hidden]"],
            "4-5* auto host rotaion", 54581109, 1418503, "tofubeats - CANDYYYLAND feat LIZ - Pa's Lam System Remix [Nathan's Extra]", "HeadToHead", "Freemod",
            [P(1, "player1", 1, true, "Host / Hidden, HardRock"), P(2, "player2[abc]", 2), P(3, "pl a ye r3", 3), P(4, "pl_a[y]er4", 4), P(5, "player5abcdefga", 5), P(6, "pla y e r 6efga", 6, options: "Hidden"), P(16, "player7abc[fga]", 7, options: "Hidden")]),

        ["team mode and ready states"] = new(
            ["Room name: 4* auto host rotaion test, History: https://osu.ppy.sh/mp/54597968",
             "Beatmap: https://osu.ppy.sh/b/1569586 Chaos City Niigata - Ukiyoe Yokochou [Insane]",
             "Team mode: TeamVs, Win condition: Score", Freemod, "Players: 2",
             "Slot 1  Ready     https://osu.ppy.sh/u/111 p1              [Host / Team Blue]",
             "Slot 2  No Map    https://osu.ppy.sh/u/222 p2              [Team Red ]"],
            "4* auto host rotaion test", 54597968, 1569586, "Chaos City Niigata - Ukiyoe Yokochou [Insane]", "TeamVs", "Freemod",
            [P(1, "p1", 111, true, "Host / Team Blue", "Ready", Team.Blue), P(2, "p2", 222, false, "Team Red", "No Map", Team.Red)]),

        ["names that look like options"] = new(
            [Room53084403, Beatmap853167, HeadToHead, Freemod, "Players: 5",
             "Slot 1  Not Ready https://osu.ppy.sh/u/8286882 gnsksz[aueie]   [Host]",
             "Slot 2  Not Ready https://osu.ppy.sh/u/10351992 Discuzz [as]v   [Hidden, DoubleTime]",
             "Slot 3  Not Ready https://osu.ppy.sh/u/13745792 Sedo sam [quit] ",
             "Slot 4  Not Ready https://osu.ppy.sh/u/7354213 Suit[__]aksas   ",
             "Slot 5  Not Ready https://osu.ppy.sh/u/13585495 -K][][a sell    "],
            "5* (´・ω・`) host rotate", 53084403, 853167, "Silent Siren - Hachigatsu no Yoru [August]", "HeadToHead", "Freemod",
            [P(1, "gnsksz[aueie]", 8286882, true, "Host"), P(2, "Discuzz [as]v", 10351992, options: "Hidden, DoubleTime"), P(3, "Sedo sam [quit]", 13745792), P(4, "Suit[__]aksas", 7354213), P(5, "-K][][a sell", 13585495)]),

        ["host, team and mods together"] = new(
            ["Room name: ahr test, History: https://osu.ppy.sh/mp/54598622",
             "Beatmap: https://osu.ppy.sh/b/86920 SID - Ranbu no Melody (TV Size) [Happy's Insane]",
             "Team mode: TeamVs, Win condition: Score", "Active mods: DoubleTime, Freemod", "Players: 1",
             "Slot 1  Not Ready https://osu.ppy.sh/u/8286882 gnsksz          [Host / Team Blue / Hidden, HardRock]"],
            "ahr test", 54598622, 86920, "SID - Ranbu no Melody (TV Size) [Happy's Insane]", "TeamVs", "DoubleTime, Freemod",
            [P(1, "gnsksz", 8286882, true, "Host / Team Blue / Hidden, HardRock", team: Team.Blue)]),
    };

    public static TheoryData<string> CaseNames => new(Cases.Keys);

    private static BanchoReader NewReader(TimeProvider? time = null, bool isPrivate = false) =>
        new(time ?? TimeProvider.System, TimeSpan.FromSeconds(1), isPrivate);

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Assembles_room_state(string name)
    {
        var testCase = Cases[name];
        var reader = NewReader();

        var results = testCase.Lines.Select(reader.Read).ToList();

        Assert.All(results[..^1], Assert.Null);
        var state = Assert.IsType<RoomStateReceived>(results[^1]).State;
        Assert.Equal(testCase.Name, state.Name);
        Assert.Equal(testCase.RoomId, state.RoomId);
        Assert.Equal(testCase.BeatmapId, state.BeatmapId);
        Assert.Equal(testCase.BeatmapTitle, state.BeatmapTitle);
        Assert.Equal(testCase.TeamMode, state.TeamMode);
        Assert.Equal("Score", state.WinCondition);
        Assert.Equal(testCase.Mods, state.ActiveMods);
        Assert.Equal(testCase.Players, state.Players);
    }

    // osu-ahr's settings promise never resolved for an empty, unchanged room.
    [Fact]
    public void Completes_room_state_with_no_players()
    {
        var reader = NewReader();
        LobbyEvent? last = null;

        foreach (var line in new[] { Room53084403, Beatmap853167, HeadToHead, Freemod, "Players: 0" })
        {
            last = reader.Read(line);
        }

        Assert.Empty(Assert.IsType<RoomStateReceived>(last).State.Players);
    }

    [Fact]
    public void Other_messages_can_arrive_during_a_room_state_reply()
    {
        var reader = NewReader();
        foreach (var line in new[] { Room53084403, Beatmap853167, HeadToHead, Freemod, "Players: 1" })
        {
            reader.Read(line);
        }

        Assert.Null(reader.Read("hello!"));
        Assert.Equal(new PlayerLeft("someone"), reader.Read("someone left the game."));

        var result = reader.Read("Slot 1  Not Ready https://osu.ppy.sh/u/1 player1         [Host]");

        Assert.Single(Assert.IsType<RoomStateReceived>(result).State.Players);
    }

    [Fact]
    public void A_new_room_reply_restarts_assembly()
    {
        var reader = NewReader();
        foreach (var line in new[] { Room53084403, Beatmap853167, HeadToHead, Freemod, "Players: 5" })
        {
            reader.Read(line);
        }

        LobbyEvent? last = null;
        foreach (var line in new[]
        {
            "Room name: ahr test, History: https://osu.ppy.sh/mp/54598622",
            "Beatmap: https://osu.ppy.sh/b/86920 SID - Ranbu no Melody (TV Size) [Happy's Insane]",
            HeadToHead, "Active mods: DoubleTime, Freemod", "Players: 1",
            "Slot 1  Not Ready https://osu.ppy.sh/u/2 0123456789abcde ",
        })
        {
            last = reader.Read(line);
        }

        var state = Assert.IsType<RoomStateReceived>(last).State;
        Assert.Equal(54598622, state.RoomId);
        Assert.Equal("0123456789abcde", Assert.Single(state.Players).Username);
    }

    [Fact]
    public void A_player_named_like_a_slot_still_joins()
    {
        Assert.Equal(new PlayerJoined("Slot 1", 2, Team.None), NewReader().Read("Slot 1 joined in slot 2."));
    }

    [Theory]
    [InlineData("Stats for (Jason)[https://osu.ppy.sh/u/7342098] is Multiplaying:", "Score:    18,163,888,782 (#1631)", "Plays:    78245 (lv100)", "Accuracy: 97.36%", "Jason", 7342098, PlayerActivity.Multiplaying, 18163888782L, 1631, 78245, 100, 97.36)]
    [InlineData("Stats for (horcrux18)[https://osu.ppy.sh/u/8778911] is Afk:", "Score:    584,565,786 (#260177)", "Plays:    5695 (lv64)", "Accuracy: 86.94%", "horcrux18", 8778911, PlayerActivity.Afk, 584565786L, 260177, 5695, 64, 86.94)]
    [InlineData("Stats for (gviz)[https://osu.ppy.sh/u/15145414] is Multiplayer:", "Score:    00 (#0)", "Plays:    7 (lv2)", "Accuracy: 0%", "gviz", 15145414, PlayerActivity.Multiplayer, 0L, 0, 7, 2, 0.0)]
    [InlineData("Stats for (Angel Arrow)[https://osu.ppy.sh/u/1970239] is Testing:", "Score:    59,315,895,109 (#1006)", "Plays:    104962 (lv102)", "Accuracy: 98.16%", "Angel Arrow", 1970239, PlayerActivity.Testing, 59315895109L, 1006, 104962, 102, 98.16)]
    [InlineData("Stats for (Foreskin)[https://osu.ppy.sh/u/3760263]:", "Score:    00 (#0)", "Plays:    1 (lv1)", "Accuracy: 0.00%", "Foreskin", 3760263, PlayerActivity.None, 0L, 0, 1, 1, 0.0)]
    public void Assembles_player_stats(string header, string score, string plays, string accuracy, string name, int id, PlayerActivity activity, long totalScore, int rank, int playCount, int level, double accuracyValue)
    {
        var reader = NewReader(isPrivate: true);

        Assert.Null(reader.Read(header));
        Assert.Null(reader.Read(score));
        Assert.Null(reader.Read(plays));
        var result = Assert.IsType<PlayerStatReceived>(reader.Read(accuracy));

        Assert.True(result.ViaPrivateMessage);
        Assert.Equal(new PlayerStat(name, id, activity, totalScore, rank, playCount, level, accuracyValue), result.Stat);
    }

    [Fact]
    public void An_unrecognized_activity_is_reported_as_unknown()
    {
        var reader = NewReader();
        reader.Read("Stats for (a)[https://osu.ppy.sh/u/1] is Dancing:");
        reader.Read("Score:    00 (#0)");
        reader.Read("Plays:    1 (lv1)");

        var result = Assert.IsType<PlayerStatReceived>(reader.Read("Accuracy: 0%"));

        Assert.Equal(PlayerActivity.Unknown, result.Stat.Activity);
    }

    [Fact]
    public void Referee_names_are_recognized_only_inside_the_list_window()
    {
        var time = new ManualTime();
        var reader = NewReader(time);

        Assert.Null(reader.Read("somename"));
        Assert.IsType<RefereeListStarted>(reader.Read("Match referees:"));
        Assert.Equal(new RefereeAdded("somename"), reader.Read("somename"));

        time.Advance(TimeSpan.FromSeconds(2));

        Assert.Null(reader.Read("somename"));
    }

    [Theory]
    [InlineData("A brand new kind of message")]
    [InlineData("has: punctuation")]
    public void Text_that_cannot_be_a_username_is_not_a_referee(string line)
    {
        var reader = NewReader(new ManualTime());
        reader.Read("Match referees:");

        Assert.Null(reader.Read(line));
    }

    [Theory]
    [InlineData("- Cylcl")]
    [InlineData("pl_a[y]er4")]
    [InlineData("0123456789abcde")]
    public void Unusual_but_valid_usernames_are_referees(string name)
    {
        var reader = NewReader(new ManualTime());
        reader.Read("Match referees:");

        Assert.Equal(new RefereeAdded(name), reader.Read(name));
    }
}
