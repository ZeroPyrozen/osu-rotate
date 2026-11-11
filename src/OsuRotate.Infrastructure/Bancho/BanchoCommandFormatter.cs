using System.Globalization;
using System.Text.RegularExpressions;
using OsuRotate.Domain;

namespace OsuRotate.Infrastructure.Bancho;

internal static partial class BanchoCommandFormatter
{
    private const string BanchoBot = "BanchoBot";
    private const int MaxTextLength = 400;
    private const int MaxRoomNameLength = 50;

    // A channel is required for everything except creating or joining a room and private messages.
    // Returns nothing when a room command arrives with no room to send it to.
    public static IReadOnlyList<OutboundMessage> Format(LobbyCommand command, string? channel) => command switch
    {
        CreateRoom c => [Control(IrcMessage.Privmsg(BanchoBot, $"!mp make {RoomName(c.Title)}"))],
        JoinRoom c => [Control($"JOIN {Channel(c.RoomId)}")],
        RequestStat { ViaPrivateMessage: true } c => [Control(IrcMessage.Privmsg(BanchoBot, $"!stat {Name(c.Username)}"))],
        Whisper c => Text(Name(c.Username), c.Text, c.Priority, null),
        _ when channel is null => [],
        Say c => Text(channel, c.Text, c.Priority, c.CoalesceKey),
        RequestStat c => [Control(IrcMessage.Privmsg(channel, $"!stat {Name(c.Username)}"))],
        _ => [Control(IrcMessage.Privmsg(channel, MpCommand(command)))],
    };

    public static string Channel(long roomId) => string.Create(CultureInfo.InvariantCulture, $"#mp_{roomId}");

    private static string MpCommand(LobbyCommand command) => command switch
    {
        CloseRoom => "!mp close",
        RequestRoomState => "!mp settings",
        RequestReferees => "!mp listrefs",
        TransferHost { OsuUserId: { } id } => Invariant($"!mp host #{id}"),
        TransferHost c => $"!mp host {Name(c.Username)}",
        ClearHost => "!mp clearhost",
        StartMatch { CountdownSeconds: { } seconds } => Invariant($"!mp start {seconds}"),
        StartMatch => "!mp start",
        AbortMatch => "!mp abort",
        AbortCountdown => "!mp aborttimer",
        KickPlayer c => $"!mp kick {Name(c.Username)}",
        InvitePlayer c => $"!mp invite {Name(c.Username)}",
        SetBeatmap c => Beatmap(c),
        SetMods c => $"!mp mods {c.Mods}",
        SetRoomName c => $"!mp name {RoomName(c.Name)}",
        SetPassword { Password: null } => "!mp password",
        SetPassword c => $"!mp password {c.Password}",
        SetSize c => Invariant($"!mp size {c.Size}"),
        SetTeamAndScoreMode { Size: { } size } c => Invariant($"!mp set {c.TeamMode} {c.ScoreMode} {size}"),
        SetTeamAndScoreMode c => Invariant($"!mp set {c.TeamMode} {c.ScoreMode}"),
        _ => throw new ArgumentOutOfRangeException(nameof(command), command.GetType().Name, "No Bancho command exists for this type."),
    };

    private static string Beatmap(SetBeatmap command)
    {
        var text = command.GameMode is { } mode ? Invariant($"!mp map {command.BeatmapId} {mode}") : Invariant($"!mp map {command.BeatmapId}");
        return command.Note is null ? text : $"{text} | {command.Note}";
    }

    private static List<OutboundMessage> Text(string target, string text, CommandPriority priority, string? coalesceKey)
    {
        var messages = new List<OutboundMessage>();
        foreach (var line in text.Split('\n'))
        {
            var clean = IrcMessage.Clean(line).Trim();
            for (var start = 0; start < clean.Length; start += MaxTextLength)
            {
                var chunk = clean.Substring(start, Math.Min(MaxTextLength, clean.Length - start));
                messages.Add(new OutboundMessage(IrcMessage.Privmsg(target, NeverACommand(chunk)), priority, coalesceKey));
            }
        }

        return messages;
    }

    // Text from players or map titles must not run as a referee command under the bot's account.
    private static string NeverACommand(string text) => text.TrimStart().StartsWith('!') ? $"Bot: {text}" : text;

    private static string RoomName(string name)
    {
        var printable = NonPrintableAscii().Replace(name, string.Empty).Trim();
        return printable.Length <= MaxRoomNameLength ? printable : printable[..MaxRoomNameLength];
    }

    private static string Name(string username) => PlayerNames.ToIrcName(username);

    private static OutboundMessage Control(string line) => new(line, CommandPriority.Control);

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);

    [GeneratedRegex("[^ -~]")]
    private static partial Regex NonPrintableAscii();
}
