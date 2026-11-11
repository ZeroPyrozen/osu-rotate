using OsuRotate.Domain;

namespace OsuRotate.Domain.Tests;

// Ported from osu-ahr's CommandParserTest chat command cases.
public class ChatCommandTests
{
    [Theory]
    [InlineData("!aioie")]
    [InlineData("!a")]
    [InlineData("!123")]
    [InlineData("!a ")]
    [InlineData("!v x")]
    [InlineData("!vv x y[v]")]
    [InlineData("*abc")]
    [InlineData("!help")]
    [InlineData("!Help")]
    [InlineData("!info")]
    [InlineData("!skip")]
    [InlineData("!SKIP")]
    [InlineData("!queue")]
    [InlineData("!q")]
    [InlineData("*skip")]
    [InlineData("*stipto")]
    [InlineData("!mp x")]
    [InlineData("!mp start")]
    [InlineData("!mp start 20")]
    public void Recognizes_commands(string message) => Assert.NotNull(ChatCommand.Parse(message));

    [Theory]
    [InlineData("!")]
    [InlineData("*")]
    [InlineData("  !asa")]
    [InlineData("!!ss")]
    [InlineData("*!v")]
    [InlineData("abc")]
    [InlineData("abc !abc")]
    [InlineData("!mp")]
    [InlineData("!mp ")]
    [InlineData("!roll")]
    [InlineData("!roll 100")]
    [InlineData("!where abc")]
    [InlineData("!faq")]
    [InlineData("!report")]
    [InlineData("!request")]
    [InlineData("!stat")]
    [InlineData("!stats")]
    public void Ignores_chat_and_BanchoBot_commands(string message) => Assert.Null(ChatCommand.Parse(message));

    [Theory]
    [InlineData("!abc", "!abc", "")]
    [InlineData("!a a", "!a", "a")]
    [InlineData("!a  a ", "!a", "a")]
    [InlineData("!a a b", "!a", "a b")]
    [InlineData("!a   a    b  ", "!a", "a    b")]
    [InlineData("!Abc", "!abc", "")]
    [InlineData("!ABC", "!abc", "")]
    [InlineData("!AbC aiueo AIUEO", "!abc", "aiueo AIUEO")]
    [InlineData("!mp start", "!start", "")]
    [InlineData("!mp start 30", "!start", "30")]
    [InlineData("!mp start 30 xx ", "!start", "30 xx")]
    public void Splits_name_and_argument(string message, string name, string argument)
    {
        Assert.Equal(new ChatCommand(name, argument), ChatCommand.Parse(message));
    }
}
