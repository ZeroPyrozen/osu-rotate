using System.Globalization;
using System.Text.RegularExpressions;
using OsuRotate.Domain;

namespace OsuRotate.Infrastructure.Bancho;

// Turns one line of BanchoBot text into an event. Multi-line replies are assembled by BanchoReader.
internal static class BanchoParser
{
    private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private static readonly Dictionary<string, LobbyEvent> ExactMessages = new(StringComparer.Ordinal)
    {
        ["Aborted the match"] = new MatchAborted(),
        ["All players are ready"] = new AllPlayersReady(),
        ["Changed the match password"] = new PasswordChanged(),
        ["Cleared match host"] = new HostCleared(),
        ["Closed the match"] = new RoomCloseAcknowledged(),
        ["Countdown aborted"] = new CountdownAborted(),
        ["Good luck, have fun!"] = new CountdownFinished(),
        ["Host is changing map..."] = new BeatmapChanging(),
        ["Invalid map ID provided"] = new InvalidBeatmapId(),
        ["Invalid or no settings provided"] = new InvalidSettings(),
        ["Invalid or no size provided"] = new InvalidSize(),
        ["Locked the match"] = new RoomLocked(),
        ["Match referees:"] = new RefereeListStarted(),
        ["No user specified"] = new NoUserSpecified(),
        ["Removed the match password"] = new PasswordRemoved(),
        ["Started the match"] = new MatchStartAcknowledged(),
        ["The match has started!"] = new MatchStarted(),
        ["The match has already been started"] = new MatchAlreadyStarted(),
        ["The match has finished!"] = new MatchFinished(),
        ["The match is not in progress"] = new MatchNotInProgress(),
        ["User not found"] = new UserNotFound(),
        ["Unlocked the match"] = new RoomUnlocked(),
    };

    // Order matters: fixed phrases come before the patterns that start with a free-form username.
    private static readonly (Regex Pattern, Func<Match, LobbyEvent> Create)[] Patterns =
    [
        (new(@"^Created the tournament match https://osu\.ppy\.sh/mp/(\d+) (.+)$", Options), m => new RoomCreated(Long(m, 1), m.Groups[2].Value)),
        (new(@"^Added (.+) to the match referees$", Options), m => new RefereeAdded(m.Groups[1].Value)),
        (new(@"^Removed (.+) from the match referees$", Options), m => new RefereeRemoved(m.Groups[1].Value)),
        (new(@"^Beatmap changed to: (.+) \(https://osu\.ppy\.sh/b/(\d+)\)$", Options), m => new BeatmapChanged(Int(m, 2), m.Groups[1].Value, ChangedByReferee: false)),
        (new(@"^Changed beatmap to https://osu\.ppy\.sh/b/(\d+) (.+)$", Options), m => new BeatmapChanged(Int(m, 1), m.Groups[2].Value, ChangedByReferee: true)),
        (new(@"^Changed match to size (\d+)$", Options), m => new RoomSizeChanged(Int(m, 1))),
        (new(@"^Changed match host to (.+)$", Options), m => new HostTransferAcknowledged(m.Groups[1].Value)),
        (new(@"^Changed match settings to\s?((\d+) slots)?,?\s?(HeadToHead|TagCoop|TeamVs|TagTeamVs)?,?\s?(ScoreV2|Score|Accuracy|Combo)?", Options), SettingsChanged),
        (new(@"^Invited (.*) to the room$", Options), m => new PlayerInvited(m.Groups[1].Value)),
        (new(@"^Kicked (.+) from the match\.?$", Options), m => new PlayerKicked(m.Groups[1].Value)),
        (new(@"^Match starts in (.+)$", Options), m => new CountdownTick(Seconds(m.Groups[1].Value))),
        (new(@"^Queued the match to start in (.+)$", Options), m => new CountdownStarted(Seconds(m.Groups[1].Value))),
        (new(@"^(.+) joined in slot (\d+)(?: for team (blue|red))?\.$", Options), m => new PlayerJoined(m.Groups[1].Value, Int(m, 2), ParseTeam(m.Groups[3].Value))),
        (new(@"^(.+) left the game\.$", Options), m => new PlayerLeft(m.Groups[1].Value)),
        (new(@"^(.+) became the host\.$", Options), m => new HostChanged(m.Groups[1].Value)),
        (new(@"^(.+) moved to slot (\d+)$", Options), m => new PlayerMoved(m.Groups[1].Value, Int(m, 2))),
        (new(@"^(.+) finished playing \(Score: (\d+), (PASSED|FAILED)\)\.$", Options), m => new PlayerFinished(m.Groups[1].Value, Long(m, 2), m.Groups[3].Value == "PASSED")),
        (new(@"^(.+) rolls (\d+) point\(s\)$", Options), m => new PlayerRolled(m.Groups[1].Value, Int(m, 2))),
        (new(@"^(.+) changed to (Blue|Red)$", Options), m => new PlayerTeamChanged(m.Groups[1].Value, ParseTeam(m.Groups[2].Value))),
    ];

    private static readonly Regex SecondsPattern = new(@"(\d+) seconds?", Options);
    private static readonly Regex MinutesPattern = new(@"(\d+) minutes?", Options);

    public static LobbyEvent? Parse(string message)
    {
        if (ExactMessages.TryGetValue(message, out var exact))
        {
            return exact;
        }

        foreach (var (pattern, create) in Patterns)
        {
            var match = pattern.Match(message);
            if (match.Success)
            {
                return create(match);
            }
        }

        return null;
    }

    private static RoomSettingsChanged SettingsChanged(Match match) => new(
        match.Groups[2].Success ? Int(match, 2) : null,
        match.Groups[3].Success ? match.Groups[3].Value : null,
        match.Groups[4].Success ? match.Groups[4].Value : null);

    private static int Seconds(string text)
    {
        var seconds = SecondsPattern.Match(text);
        var minutes = MinutesPattern.Match(text);
        return (seconds.Success ? Int(seconds, 1) : 0) + ((minutes.Success ? Int(minutes, 1) : 0) * 60);
    }

    private static Team ParseTeam(string team) => team.ToUpperInvariant() switch
    {
        "BLUE" => Team.Blue,
        "RED" => Team.Red,
        _ => Team.None,
    };

    private static int Int(Match match, int group) => int.Parse(match.Groups[group].ValueSpan, CultureInfo.InvariantCulture);

    private static long Long(Match match, int group) => long.Parse(match.Groups[group].ValueSpan, CultureInfo.InvariantCulture);
}
