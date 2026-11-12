using OsuRotate.Domain;
using OsuRotate.Infrastructure.Bancho;

namespace OsuRotate.Bancho.Tests;

public class BanchoCommandFormatterTests
{
    private const string Channel = "#mp_123";

    public static TheoryData<LobbyCommand, string> Commands => new()
    {
        { new CreateRoom("5* rotate"), "PRIVMSG BanchoBot :!mp make 5* rotate" },
        { new JoinRoom(123), "JOIN #mp_123" },
        { new CloseRoom(), "PRIVMSG #mp_123 :!mp close" },
        { new RequestRoomState(), "PRIVMSG #mp_123 :!mp settings" },
        { new RequestReferees(), "PRIVMSG #mp_123 :!mp listrefs" },
        { new RequestStat("some one", true), "PRIVMSG BanchoBot :!stat some_one" },
        { new RequestStat("some one", false), "PRIVMSG #mp_123 :!stat some_one" },
        { new TransferHost("some one", 42), "PRIVMSG #mp_123 :!mp host #42" },
        { new TransferHost("some one"), "PRIVMSG #mp_123 :!mp host some_one" },
        { new ClearHost(), "PRIVMSG #mp_123 :!mp clearhost" },
        { new StartMatch(), "PRIVMSG #mp_123 :!mp start" },
        { new StartMatch(30), "PRIVMSG #mp_123 :!mp start 30" },
        { new AbortMatch(), "PRIVMSG #mp_123 :!mp abort" },
        { new AbortCountdown(), "PRIVMSG #mp_123 :!mp aborttimer" },
        { new KickPlayer("some one"), "PRIVMSG #mp_123 :!mp kick some_one" },
        { new InvitePlayer("some one"), "PRIVMSG #mp_123 :!mp invite some_one" },
        { new SetBeatmap(100), "PRIVMSG #mp_123 :!mp map 100" },
        { new SetBeatmap(100, 0), "PRIVMSG #mp_123 :!mp map 100 0" },
        { new SetBeatmap(100, 0, "Stars: 5"), "PRIVMSG #mp_123 :!mp map 100 0 | Stars: 5" },
        { new SetMods("HR DT"), "PRIVMSG #mp_123 :!mp mods HR DT" },
        { new SetRoomName("my room"), "PRIVMSG #mp_123 :!mp name my room" },
        { new SetPassword("abc"), "PRIVMSG #mp_123 :!mp password abc" },
        { new SetPassword(null), "PRIVMSG #mp_123 :!mp password" },
        { new SetSize(8), "PRIVMSG #mp_123 :!mp size 8" },
        { new SetTeamAndScoreMode(0, 3), "PRIVMSG #mp_123 :!mp set 0 3" },
        { new SetTeamAndScoreMode(2, 0, 16), "PRIVMSG #mp_123 :!mp set 2 0 16" },
        { new Say("hello"), "PRIVMSG #mp_123 :hello" },
        { new Whisper("some one", "hello"), "PRIVMSG some_one :hello" },
    };

    [Theory]
    [MemberData(nameof(Commands))]
    public void Formats_command(LobbyCommand command, string expected)
    {
        var message = Assert.Single(BanchoCommandFormatter.Format(command, Channel));

        Assert.Equal(expected, message.Line);
    }

    [Theory]
    [InlineData("!mp close")]
    [InlineData("  !mp kick owner")]
    [InlineData("!stat someone")]
    public void Said_text_can_never_run_as_a_referee_command(string text)
    {
        var message = Assert.Single(BanchoCommandFormatter.Format(new Say(text), Channel));

        Assert.StartsWith("PRIVMSG #mp_123 :Bot: !", message.Line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_later_chunk_of_long_text_can_not_run_as_a_command_either()
    {
        var text = new string('a', 400) + "!mp close";

        var messages = BanchoCommandFormatter.Format(new Say(text), Channel);

        Assert.Equal(2, messages.Count);
        Assert.Equal("PRIVMSG #mp_123 :Bot: !mp close", messages[1].Line);
    }

    [Fact]
    public void Multi_line_text_becomes_one_message_per_line()
    {
        var messages = BanchoCommandFormatter.Format(new Say("one\r\n\r\ntwo"), Channel);

        Assert.Equal(["PRIVMSG #mp_123 :one", "PRIVMSG #mp_123 :two"], messages.Select(m => m.Line));
    }

    [Fact]
    public void Room_names_are_limited_to_50_printable_ascii_characters()
    {
        var message = Assert.Single(BanchoCommandFormatter.Format(new CreateRoom("日本語room" + new string('x', 60)), null));

        Assert.Equal("PRIVMSG BanchoBot :!mp make room" + new string('x', 46), message.Line);
    }

    // These are answered by the application; sending one to Bancho is a bug.
    [Fact]
    public void Application_commands_are_never_formatted_for_bancho()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BanchoCommandFormatter.Format(new LookUpBeatmap(1, GameMode.Osu, true), Channel));
        Assert.Throws<ArgumentOutOfRangeException>(() => BanchoCommandFormatter.Format(new ReadMatchHistory(5), Channel));
        Assert.Throws<ArgumentOutOfRangeException>(() => BanchoCommandFormatter.Format(
            new RecordMatch(new MatchRecord(1, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, MatchOutcome.Finished, [])), Channel));
    }

    // osu-ahr's LobbyTest "remove invalid chars from lobby title" cases. This also trims the ends.
    [Theory]
    [InlineData("a", "a")]
    [InlineData("asdflkj", "asdflkj")]
    [InlineData(" $% BN |~=", "$% BN |~=")]
    [InlineData("4-5 | alt | test @join", "4-5 | alt | test @join")]
    [InlineData("あいうおaaa", "aaa")]
    [InlineData("aa\n\raa", "aaaa")]
    public void Room_names_keep_only_printable_ascii(string name, string expected)
    {
        var message = Assert.Single(BanchoCommandFormatter.Format(new SetRoomName(name), Channel));
        Assert.Equal($"PRIVMSG {Channel} :!mp name {expected}", message.Line);
    }

    [Fact]
    public void Line_breaks_in_arguments_cannot_inject_commands()
    {
        var message = Assert.Single(BanchoCommandFormatter.Format(new SetPassword("abc\r\nQUIT"), Channel));

        Assert.DoesNotContain('\n', message.Line);
        Assert.DoesNotContain('\r', message.Line);
    }

    [Fact]
    public void Room_commands_produce_nothing_without_a_room()
    {
        Assert.Empty(BanchoCommandFormatter.Format(new StartMatch(), null));
        Assert.Empty(BanchoCommandFormatter.Format(new Say("hello"), null));
    }

    [Theory]
    [InlineData(CommandPriority.Admin)]
    [InlineData(CommandPriority.Info)]
    public void Say_keeps_its_priority_and_coalesce_key(CommandPriority priority)
    {
        var message = Assert.Single(BanchoCommandFormatter.Format(new Say("x", priority, "queue"), Channel));

        Assert.Equal(priority, message.Priority);
        Assert.Equal("queue", message.CoalesceKey);
    }

    [Fact]
    public void Game_commands_use_control_priority()
    {
        Assert.Equal(CommandPriority.Control, Assert.Single(BanchoCommandFormatter.Format(new TransferHost("a"), Channel)).Priority);
    }
}
