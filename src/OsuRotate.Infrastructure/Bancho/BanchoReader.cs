using System.Globalization;
using System.Text.RegularExpressions;
using OsuRotate.Domain;

namespace OsuRotate.Infrastructure.Bancho;

// Reads BanchoBot's messages from one source (a channel or private messages) in order, assembling
// the replies that span several lines. Returns null while a reply is incomplete or a line is unknown.
internal sealed class BanchoReader(TimeProvider time, TimeSpan refereeListWindow, bool isPrivateMessages = false)
{
    private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private static readonly Regex RoomName = new(@"^Room name: (.+), History: .+?(\d+)$", Options);
    private static readonly Regex Beatmap = new(@"^Beatmap: \S+?(\d+) (.+)$", Options);
    private static readonly Regex TeamMode = new(@"^Team mode: (.+), Win condition: (.+)$", Options);
    private static readonly Regex ActiveMods = new(@"^Active mods: (.+)$", Options);
    private static readonly Regex PlayerCount = new(@"^Players: (\d+)$", Options);
    private static readonly Regex Slot = new(@"^Slot (\d+)\s+(.+) https://osu\.ppy\.sh/u/(\d+) (.{15})\s*(?:\[(.+)\])?$", Options);

    private static readonly Regex StatHeader = new(@"^Stats for \((.+)\)\[https://osu\.ppy\.sh/u/(\d+)\](?: is (.+))?:$", Options);
    private static readonly Regex StatScore = new(@"^Score:\s+([\d,]+)\s+\(#(\d+)\)$", Options);
    private static readonly Regex StatPlays = new(@"^Plays:\s+(\d+)\s+\(lv(\d+)\)$", Options);
    private static readonly Regex StatAccuracy = new(@"^Accuracy: ([\d.]+)%$", Options);

    // osu!'s username rules: these characters, 15 at most.
    private static readonly Regex Username = new(@"^[A-Za-z0-9\-\[\]_ ]{1,15}$", Options);

    private RoomDraft? _room;
    private StatDraft? _stat;
    private DateTimeOffset _refereeListEnds = DateTimeOffset.MinValue;

    public LobbyEvent? Read(string message)
    {
        if (TryReadRoomState(message, out var roomState))
        {
            return roomState;
        }

        if (TryReadStat(message, out var stat))
        {
            return stat;
        }

        var parsed = BanchoParser.Parse(message);
        if (parsed is RefereeListStarted)
        {
            _refereeListEnds = time.GetUtcNow() + refereeListWindow;
        }

        // The referee list is bare usernames with nothing to mark where it ends, so it is time-boxed.
        if (parsed is null && time.GetUtcNow() < _refereeListEnds && Username.IsMatch(message))
        {
            return new RefereeAdded(message);
        }

        return parsed;
    }

    private bool TryReadRoomState(string message, out LobbyEvent? result)
    {
        result = null;

        var match = RoomName.Match(message);
        if (match.Success)
        {
            _room = new RoomDraft { Name = match.Groups[1].Value, RoomId = long.Parse(match.Groups[2].ValueSpan, CultureInfo.InvariantCulture) };
            return true;
        }

        if (_room is null)
        {
            return false;
        }

        if ((match = Beatmap.Match(message)).Success)
        {
            _room.BeatmapId = int.Parse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
            _room.BeatmapTitle = match.Groups[2].Value;
            return true;
        }

        if ((match = TeamMode.Match(message)).Success)
        {
            _room.TeamMode = match.Groups[1].Value;
            _room.WinCondition = match.Groups[2].Value;
            return true;
        }

        if ((match = ActiveMods.Match(message)).Success)
        {
            _room.ActiveMods = match.Groups[1].Value;
            return true;
        }

        if ((match = PlayerCount.Match(message)).Success)
        {
            _room.ExpectedPlayers = int.Parse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
            result = CompleteRoomIfReady();
            return true;
        }

        if ((match = Slot.Match(message)).Success)
        {
            _room.Players.Add(ParseSlot(match));
            result = CompleteRoomIfReady();
            return true;
        }

        return false;
    }

    private RoomStateReceived? CompleteRoomIfReady()
    {
        if (_room is null || _room.ExpectedPlayers is not { } expected || _room.Players.Count != expected)
        {
            return null;
        }

        var state = new RoomState(_room.Name, _room.RoomId, _room.BeatmapId, _room.BeatmapTitle, _room.TeamMode, _room.WinCondition, _room.ActiveMods, _room.Players);
        _room = null;
        return new RoomStateReceived(state);
    }

    private static RoomPlayer ParseSlot(Match match)
    {
        var options = match.Groups[5].Success ? match.Groups[5].Value.Trim() : string.Empty;
        var team = !options.Contains("Team", StringComparison.Ordinal) ? Team.None
            : options.Contains("Blue", StringComparison.Ordinal) ? Team.Blue
            : Team.Red;

        return new RoomPlayer(
            int.Parse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture),
            match.Groups[2].Value.Trim(),
            int.Parse(match.Groups[3].ValueSpan, CultureInfo.InvariantCulture),
            match.Groups[4].Value.Trim(),
            options.Contains("Host", StringComparison.Ordinal),
            team,
            options);
    }

    private bool TryReadStat(string message, out LobbyEvent? result)
    {
        result = null;

        var match = StatHeader.Match(message);
        if (match.Success)
        {
            _stat = new StatDraft
            {
                Username = match.Groups[1].Value,
                OsuUserId = int.Parse(match.Groups[2].ValueSpan, CultureInfo.InvariantCulture),
                Activity = ParseActivity(match.Groups[3].Value),
            };
            return true;
        }

        if (_stat is null)
        {
            return false;
        }

        if ((match = StatScore.Match(message)).Success)
        {
            _stat.Score = long.Parse(match.Groups[1].Value.Replace(",", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture);
            _stat.Rank = int.Parse(match.Groups[2].ValueSpan, CultureInfo.InvariantCulture);
            return true;
        }

        if ((match = StatPlays.Match(message)).Success)
        {
            _stat.Plays = int.Parse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
            _stat.Level = int.Parse(match.Groups[2].ValueSpan, CultureInfo.InvariantCulture);
            return true;
        }

        if ((match = StatAccuracy.Match(message)).Success)
        {
            var accuracy = double.Parse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
            var stat = new PlayerStat(_stat.Username, _stat.OsuUserId, _stat.Activity, _stat.Score, _stat.Rank, _stat.Plays, _stat.Level, accuracy);
            _stat = null;
            result = new PlayerStatReceived(stat, isPrivateMessages);
            return true;
        }

        return false;
    }

    private static PlayerActivity ParseActivity(string activity) =>
        activity.Length == 0 ? PlayerActivity.None
        : Enum.TryParse<PlayerActivity>(activity, out var parsed) ? parsed
        : PlayerActivity.Unknown;

    private sealed class RoomDraft
    {
        public required string Name { get; init; }
        public required long RoomId { get; init; }
        public int BeatmapId { get; set; }
        public string BeatmapTitle { get; set; } = string.Empty;
        public string TeamMode { get; set; } = string.Empty;
        public string WinCondition { get; set; } = string.Empty;
        public string ActiveMods { get; set; } = string.Empty;
        public int? ExpectedPlayers { get; set; }
        public List<RoomPlayer> Players { get; } = [];
    }

    private sealed class StatDraft
    {
        public required string Username { get; init; }
        public required int OsuUserId { get; init; }
        public required PlayerActivity Activity { get; init; }
        public long Score { get; set; }
        public int Rank { get; set; }
        public int Plays { get; set; }
        public int Level { get; set; }
    }
}
