using OsuRotate.Infrastructure.Bancho;

namespace OsuRotate.Bancho.Tests;

public class IrcMessageTests
{
    [Fact]
    public void Parses_a_channel_message_and_keeps_trailing_spaces()
    {
        var message = IrcMessage.Parse(":BanchoBot!cho@ppy.sh PRIVMSG #mp_123 :Slot 3  Not Ready https://osu.ppy.sh/u/3 p3              ");

        Assert.NotNull(message);
        Assert.Equal("BanchoBot", message.Nick);
        Assert.Equal("PRIVMSG", message.Command);
        Assert.Equal(["#mp_123", "Slot 3  Not Ready https://osu.ppy.sh/u/3 p3              "], message.Parameters);
    }

    [Theory]
    [InlineData("PING cho.ppy.sh", "cho.ppy.sh")]
    [InlineData("PING :cho.ppy.sh", "cho.ppy.sh")]
    public void Parses_ping_without_a_prefix(string line, string parameter)
    {
        var message = IrcMessage.Parse(line);

        Assert.NotNull(message);
        Assert.Null(message.Prefix);
        Assert.Equal("PING", message.Command);
        Assert.Equal([parameter], message.Parameters);
    }

    [Fact]
    public void Parses_a_numeric_reply_with_a_server_prefix()
    {
        var message = IrcMessage.Parse(":cho.ppy.sh 403 creator #mp_1 :No such channel #mp_1");

        Assert.NotNull(message);
        Assert.Equal("403", message.Command);
        Assert.Equal(["creator", "#mp_1", "No such channel #mp_1"], message.Parameters);
    }

    [Fact]
    public void Colons_inside_the_trailing_parameter_are_kept()
    {
        var message = IrcMessage.Parse(":a!b@c PRIVMSG #mp_1 :see: https://osu.ppy.sh :)");

        Assert.NotNull(message);
        Assert.Equal("see: https://osu.ppy.sh :)", message.Parameters[1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(":onlyprefix")]
    public void Rejects_lines_with_no_command(string line)
    {
        Assert.Null(IrcMessage.Parse(line));
    }

    [Fact]
    public void Privmsg_strips_line_breaks_so_text_cannot_inject_commands()
    {
        var line = IrcMessage.Privmsg("#mp_1", "hello\r\nQUIT :bye");

        Assert.Equal("PRIVMSG #mp_1 :helloQUIT :bye", line);
    }
}
