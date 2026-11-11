using System.Text;

namespace OsuRotate.Infrastructure.Bancho;

internal sealed record IrcMessage(string? Prefix, string Command, IReadOnlyList<string> Parameters)
{
    public string? Nick => Prefix?.Split('!', 2)[0];

    public static IrcMessage? Parse(string line)
    {
        var rest = line.AsSpan().TrimEnd('\r');
        string? prefix = null;

        if (rest.StartsWith(":"))
        {
            var space = rest.IndexOf(' ');
            if (space < 0)
            {
                return null;
            }

            prefix = rest[1..space].ToString();
            rest = rest[(space + 1)..].TrimStart(' ');
        }

        var parameters = new List<string>();
        string? command = null;

        while (!rest.IsEmpty)
        {
            if (command is not null && rest[0] == ':')
            {
                // Everything after the colon is one parameter, trailing spaces included.
                parameters.Add(rest[1..].ToString());
                break;
            }

            var space = rest.IndexOf(' ');
            var token = space < 0 ? rest : rest[..space];
            rest = space < 0 ? [] : rest[(space + 1)..].TrimStart(' ');

            if (command is null)
            {
                command = token.ToString();
            }
            else
            {
                parameters.Add(token.ToString());
            }
        }

        return command is null ? null : new IrcMessage(prefix, command, parameters);
    }

    public static string Privmsg(string target, string text) => $"PRIVMSG {Clean(target)} :{Clean(text)}";

    // Control characters are removed because CR or LF in user text would inject a raw IRC command.
    public static string Clean(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (!char.IsControl(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
