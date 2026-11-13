using OsuRotate.Application;
using OsuRotate.Domain;

namespace OsuRotate.Application.Tests;

// Phase 6, issue 05: the journal trace. It must show what the lobby did without writing chat.
public class LobbyTraceTests
{
    [Theory]
    [InlineData("gg wp everyone, my address is 1 Secret Street", "chat from alice")]
    [InlineData("!skip", "chat from alice (!skip)")]
    [InlineData("!SKIP please, this map is too hard for my secret reasons", "chat from alice (!skip)")]
    [InlineData("  !queue", "chat from alice (!queue)")]
    [InlineData("!", "chat from alice")]
    [InlineData("!skip2 x", "chat from alice")]
    [InlineData("!thisisaverylongcommandword", "chat from alice")]
    [InlineData("nice !skip", "chat from alice")]
    public void A_chat_line_shows_who_and_at_most_its_command_word(string text, string expected)
    {
        Assert.Equal(expected, LobbyTrace.Describe(new ChatReceived("alice", text, IsAction: false)));
        Assert.Equal(expected.Replace("chat from", "private message from", StringComparison.Ordinal), LobbyTrace.Describe(new PrivateMessageReceived("alice", text)));
    }

    [Fact]
    public void Chat_text_never_reaches_a_trace_line()
    {
        const string secret = "my password is hunter2";
        var lines = new[]
        {
            LobbyTrace.Describe(new BanchoEvent(new ChatReceived("alice", secret, IsAction: false))),
            LobbyTrace.Describe(new BanchoEvent(new ChatReceived("alice", "!skip " + secret, IsAction: true))),
            LobbyTrace.Describe(new BanchoEvent(new PrivateMessageReceived("alice", secret))),
            LobbyTrace.Describe(new SayInLobby(secret)),
        };

        Assert.All(lines, line =>
        {
            Assert.NotNull(line);
            Assert.DoesNotContain("hunter2", line, StringComparison.Ordinal);
            Assert.DoesNotContain("password", line, StringComparison.Ordinal);
        });
        Assert.Equal("owner: chat (22 characters)", lines[3]);
    }

    [Fact]
    public void The_room_password_is_never_shown()
    {
        Assert.Equal("!mp password (set, not shown)", LobbyTrace.Describe(new SetPassword("hunter2")));
        Assert.Equal("!mp password (removed)", LobbyTrace.Describe(new SetPassword(null)));
        Assert.Equal("!mp password (removed)", LobbyTrace.Describe(new SetPassword(string.Empty)));
    }

    [Theory]
    [MemberData(nameof(Events))]
    public void Lobby_events_read_as_what_happened(LobbyEvent lobbyEvent, string expected)
    {
        Assert.Equal(expected, LobbyTrace.Describe(lobbyEvent));
        Assert.Equal(expected, LobbyTrace.Describe(new BanchoEvent(lobbyEvent)));
    }

    public static TheoryData<LobbyEvent, string> Events() => new()
    {
        { new PlayerJoined("alice", 3, Team.None), "joined: alice (slot 3)" },
        { new PlayerLeft("alice"), "left: alice" },
        { new PlayerKicked("bob"), "kicked: bob" },
        { new HostChanged("alice"), "host is now alice" },
        { new HostCleared(), "host cleared" },
        { new BeatmapChanged(75, "Artist - Title [Hard]", ChangedByReferee: false), "map picked: 75 \"Artist - Title [Hard]\"" },
        { new BeatmapChanged(75, "x", ChangedByReferee: true), "map picked: 75 \"x\" (by referee)" },
        {
            new BeatmapLookedUp(75, BeatmapLookup.Found(new BeatmapInfo(75, 1, "t", GameMode.Osu, 5.4321, 125, "ranked"))),
            "map 75 looked up: Found, Osu, 5.43*, 125 s"
        },
        { new BeatmapLookedUp(76, new BeatmapLookup(BeatmapLookupOutcome.NotFound)), "map 76 looked up: NotFound" },
        { new MatchStarted(), "match started" },
        { new MatchFinished(), "match finished" },
        { new MatchAborted(), "match aborted" },
        { new AllPlayersReady(), "all players ready" },
        { new PlayerFinished("alice", 812345, Passed: true), "finished: alice 812345" },
        { new PlayerFinished("bob", 1000, Passed: false), "finished: bob 1000 (failed)" },
        { new CountdownStarted(30), "countdown 30 s" },
        { new RoomCreated(123, "auto host"), "room 123 created: \"auto host\"" },
        { new RoomJoinFailed(123, "closed"), "could not join room 123: closed" },
        { new TransportDisconnected("timeout"), "disconnected from Bancho: timeout" },
        { new MatchHistoryRead([new SeenKick("x", DateTimeOffset.UnixEpoch)], 99), "match history read: 1 kicks, up to event 99" },
        {
            new RoomStateReceived(new RoomState("room", 1, 75, "t", "HeadToHead", "Score", string.Empty, [
                new RoomPlayer(1, "Ready", 10, "alice", IsHost: false, Team.None, string.Empty),
                new RoomPlayer(2, "Ready", 11, "bob", IsHost: true, Team.None, string.Empty),
            ])),
            "room state: 2 players, host bob, map 75"
        },
    };

    [Fact]
    public void Countdown_ticks_are_left_out()
    {
        Assert.Null(LobbyTrace.Describe(new CountdownTick(10)));
        Assert.Null(LobbyTrace.Describe(new BanchoEvent(new CountdownTick(10))));
    }

    [Theory]
    [MemberData(nameof(Commands))]
    public void Commands_read_as_what_the_bot_sent(LobbyCommand command, string expected) =>
        Assert.Equal(expected, LobbyTrace.Describe(command));

    public static TheoryData<LobbyCommand, string> Commands() => new()
    {
        { new TransferHost("alice"), "!mp host alice" },
        { new StartMatch(), "!mp start" },
        { new StartMatch(30), "!mp start 30" },
        { new AbortMatch(), "!mp abort" },
        { new KickPlayer("bob"), "!mp kick bob" },
        { new SetBeatmap(75), "!mp map 75" },
        { new SetBeatmap(75, 0), "!mp map 75 0" },
        { new SetMods("Freemod"), "!mp mods Freemod" },
        { new SetSize(16), "!mp size 16" },
        { new SetRoomName("auto host"), "!mp name \"auto host\"" },
        { new LookUpBeatmap(75, GameMode.Osu, AllowConvert: true), "look up map 75 (Osu, converts allowed)" },
        { new Say("Queue: alice, bob"), "say: Queue: alice, bob" },
        { new Say("line one\nline two\n"), "say: line one / line two" },
        { new Whisper("alice", "You are next."), "whisper to alice: You are next." },
        { new RequestRoomState(), "!mp settings" },
        { new ClearHost(), "!mp clearhost" },
    };

    [Fact]
    public void A_long_announcement_is_cut()
    {
        var line = LobbyTrace.Describe(new Say(new string('x', 1000)));
        Assert.Equal("say: ".Length + 300 + "...".Length, line.Length);
        Assert.EndsWith("...", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_match_record_is_summed_up()
    {
        var match = new MatchRecord(75, "alice", 10, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, MatchOutcome.Finished,
            [new MatchRecordScore("alice", 10, 5, true), new MatchRecordScore("bob", 11, 4, false)]);
        Assert.Equal("record match: map 75, host alice, Finished, 2 scores", LobbyTrace.Describe(new RecordMatch(match)));
    }

    [Theory]
    [MemberData(nameof(OwnerInputs))]
    public void Owner_actions_say_so(LobbyInput input, string expected) => Assert.Equal(expected, LobbyTrace.Describe(input));

    public static TheoryData<LobbyInput, string> OwnerInputs() => new()
    {
        { new SkipHost(), "owner: skip host" },
        { new SkipToPlayer("bob"), "owner: host to bob" },
        { new DenyPlayer("eve"), "owner: deny eve" },
        { new ForceStart(10), "owner: start match in 10 s" },
        { new ForceAbort(), "owner: abort match" },
        { new CreateLobby("auto host"), "owner: create room \"auto host\"" },
        { new AttachLobby(123), "owner: attach to room 123" },
        { new CloseLobbyWhenEmpty(true), "owner: close room when empty" },
        { new KickFromLobby("bob"), "owner: kick bob" },
        { new ChangeSettings(new LobbySettings { OwnerName = "creator" }), "owner: rules changed" },
    };
}
