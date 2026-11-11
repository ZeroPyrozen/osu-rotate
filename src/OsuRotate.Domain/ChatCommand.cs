using System.Text.RegularExpressions;

namespace OsuRotate.Domain;

public sealed partial record ChatCommand(string Name, string Argument)
{
    // A command starts with ! or *. BanchoBot's own commands are left alone, and "!mp x" from a
    // player is read as "!x", because players often type it out of habit.
    public static ChatCommand? Parse(string message)
    {
        var text = message.TrimEnd();
        if (text.Equals("!mp", StringComparison.OrdinalIgnoreCase) || !IsCommand().IsMatch(text))
        {
            return null;
        }

        var mp = MpPrefix().Match(text);
        if (mp.Success)
        {
            return new ChatCommand($"!{mp.Groups[1].Value.ToLowerInvariant()}", mp.Groups[2].Value);
        }

        var plain = Plain().Match(text);
        return plain.Success ? new ChatCommand(plain.Groups[1].Value.ToLowerInvariant(), plain.Groups[2].Value) : null;
    }

    [GeneratedRegex(@"^[!*](?!roll|stats?|where|faq|report|request)\w+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IsCommand();

    [GeneratedRegex(@"^!mp\s+(\w+)\s*(.*?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MpPrefix();

    [GeneratedRegex(@"^([!*]\w+)\s*(.*?)$", RegexOptions.CultureInvariant)]
    private static partial Regex Plain();
}
