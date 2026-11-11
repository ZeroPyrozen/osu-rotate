using System.Globalization;
using System.Text.RegularExpressions;

namespace OsuRotate.Domain;

// Values are what !mp set takes.
public enum TeamMode
{
    HeadToHead = 0,
    TagCoop = 1,
    TeamVs = 2,
    TagTeamVs = 3,
}

public enum ScoreMode
{
    Score = 0,
    Accuracy = 1,
    Combo = 2,
    ScoreV2 = 3,
}

[Flags]
public enum Mods : long
{
    None = 0,
    Freemod = 1L << 0,
    NoFail = 1L << 1,
    Easy = 1L << 2,
    Hidden = 1L << 3,
    HardRock = 1L << 4,
    SuddenDeath = 1L << 5,
    DoubleTime = 1L << 6,
    Nightcore = 1L << 7,
    Relax = 1L << 8,
    HalfTime = 1L << 9,
    Flashlight = 1L << 10,
    SpunOut = 1L << 11,
    Relax2 = 1L << 12,
    FadeIn = 1L << 13,
    Random = 1L << 14,
    KeyCoop = 1L << 15,
    Mirror = 1L << 16,
    Key1 = 1L << 17,
    Key2 = 1L << 18,
    Key3 = 1L << 19,
    Key4 = 1L << 20,
    Key5 = 1L << 21,
    Key6 = 1L << 22,
    Key7 = 1L << 23,
    Key8 = 1L << 24,
    Key9 = 1L << 25,
}

// Names as osu-ahr's Modes.ts reads them: the !mp value, the name !mp settings prints, and aliases.
// Spaces and hyphens are ignored and case does not matter.
public static partial class RoomModes
{
    private sealed record ModInfo(Mods Mod, string Value, bool Global, params string[] Aliases);

    private static readonly ModInfo[] ModTable =
    [
        new(Mods.Freemod, "Freemod", true),
        new(Mods.NoFail, "nf", false),
        new(Mods.Easy, "ez", false),
        new(Mods.Hidden, "hd", false),
        new(Mods.HardRock, "hr", false),
        new(Mods.SuddenDeath, "sd", false),
        new(Mods.DoubleTime, "dt", true, "double"),
        new(Mods.Nightcore, "nc", true),
        new(Mods.Relax, "relax", false, "rx"),
        new(Mods.HalfTime, "ht", true),
        new(Mods.Flashlight, "fl", false),
        new(Mods.SpunOut, "so", false),
        new(Mods.Relax2, "ap", false, "autopilot"),
        new(Mods.FadeIn, "fi", false),
        new(Mods.Random, "rd", true),
        new(Mods.KeyCoop, "co-op", false),
        new(Mods.Mirror, "mr", false),
        new(Mods.Key1, "1k", false),
        new(Mods.Key2, "2k", false),
        new(Mods.Key3, "3k", false),
        new(Mods.Key4, "4k", false),
        new(Mods.Key5, "5k", false),
        new(Mods.Key6, "6k", false),
        new(Mods.Key7, "7k", false),
        new(Mods.Key8, "8k", false),
        new(Mods.Key9, "9k", false),
    ];

    private static string Normalize(string text) =>
        text.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    public static bool TryParseTeamMode(string text, out TeamMode mode) => TryParse(text, out mode);

    public static bool TryParseScoreMode(string text, out ScoreMode mode) => TryParse(text, out mode);

    private static bool TryParse<T>(string text, out T value)
        where T : struct, Enum
    {
        var normalized = Normalize(text);
        foreach (var candidate in Enum.GetValues<T>())
        {
            if (normalized == Normalize(candidate.ToString())
                || normalized == Convert.ToInt32(candidate, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture))
            {
                value = candidate;
                return true;
            }
        }

        value = default;
        return false;
    }

    // Unknown words are ignored, as Bancho ignores them in !mp mods.
    public static Mods ParseMods(string text)
    {
        var mods = Mods.None;
        foreach (Match word in ModWord().Matches(text))
        {
            var normalized = Normalize(word.Value);
            var info = ModTable.FirstOrDefault(m =>
                Normalize(m.Value) == normalized || Normalize(m.Mod.ToString()) == normalized || m.Aliases.Any(a => Normalize(a) == normalized));
            if (info is not null)
            {
                mods |= info.Mod;
            }
        }

        return RemoveInvalidCombinations(mods);
    }

    // The same pruning as osu-ahr, so the kept mods match what Bancho reports back.
    public static Mods RemoveInvalidCombinations(Mods mods)
    {
        if (mods.HasFlag(Mods.Freemod))
        {
            foreach (var info in ModTable.Where(m => !m.Global))
            {
                mods &= ~info.Mod;
            }
        }

        if (mods.HasFlag(Mods.Easy))
        {
            mods &= ~Mods.HardRock;
        }

        if (mods.HasFlag(Mods.NoFail))
        {
            mods &= ~(Mods.SuddenDeath | Mods.Relax | Mods.Relax2);
        }

        if (mods.HasFlag(Mods.HalfTime))
        {
            mods &= ~(Mods.DoubleTime | Mods.Nightcore);
        }

        if (mods.HasFlag(Mods.SuddenDeath))
        {
            mods &= ~(Mods.Relax | Mods.Relax2);
        }

        if (mods.HasFlag(Mods.Nightcore))
        {
            mods |= Mods.DoubleTime;
        }

        if (mods.HasFlag(Mods.Relax))
        {
            mods &= ~Mods.Relax2;
        }

        return mods;
    }

    // What !mp mods takes. Bancho clears the mods when none of the words is a mod.
    public static string ToMpValue(this Mods mods) =>
        mods == Mods.None ? "None" : string.Join(' ', ModTable.Where(m => mods.HasFlag(m.Mod)).Select(m => m.Value));

    public static string ToNames(this Mods mods) =>
        mods == Mods.None ? "None" : string.Join(", ", ModTable.Where(m => mods.HasFlag(m.Mod)).Select(m => m.Mod.ToString()));

    [GeneratedRegex("[a-zA-Z0-9-]+", RegexOptions.CultureInvariant)]
    private static partial Regex ModWord();
}

public sealed record KeptMode(TeamMode Team, ScoreMode Score);

// What the lobby keeper holds the room to. Null (or Size 0) means that setting is left alone. An empty
// password means "keep the room open".
public sealed record RoomKeeping
{
    public const int MaxTitleLength = 50;

    public KeptMode? Mode { get; init; }
    public int Size { get; init; }
    public string? Password { get; init; }
    public Mods? Mods { get; init; }
    public string? Title { get; init; }

    public bool AnyChecked => Mode is not null || Mods is not null || Title is not null;

    // The password is never shown, since this text is said in the lobby.
    public string Describe()
    {
        var keeps = new List<string>();
        if (Mode is { } mode)
        {
            keeps.Add($"Mode: {mode.Team}, {mode.Score}");
        }

        if (Size != 0)
        {
            keeps.Add(string.Create(CultureInfo.InvariantCulture, $"Size: {Size}"));
        }

        if (Password is not null)
        {
            keeps.Add(Password.Length > 0 ? "Password: set" : "Password: none");
        }

        if (Mods is { } mods)
        {
            keeps.Add($"Mod(s): {mods.ToNames()}");
        }

        if (Title is not null)
        {
            keeps.Add($"Title: {Title}");
        }

        return string.Join(", ", keeps);
    }
}

// Which room setting a keep command wants put right straight away.
public enum KeepFix
{
    None,
    ModeAndSize,
    Mods,
    Password,
    Title,
}

public sealed record KeepChange(RoomKeeping After, string Message, KeepFix Fix);

// The owner's *keep and *no keep commands, ported from osu-ahr's LobbyKeeper.processCommand.
// Returns null when the command is not understood, which leaves the setting as it was.
public static partial class KeepCommand
{
    public const int MaxSize = 16;

    public static KeepChange? Apply(RoomKeeping current, string command, string argument)
    {
        var text = argument.Trim();
        return command switch
        {
            "*keep" => Keep(current, text),
            "*no" => Stop(current, text),
            _ => null,
        };
    }

    private static KeepChange? Keep(RoomKeeping current, string text)
    {
        Match match;
        if ((match = ModeArgument().Match(text)).Success)
        {
            if (!match.Groups[1].Success)
            {
                return null;
            }

            var value = match.Groups[1].Value;
            if (value.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                return new KeepChange(current with { Mode = null }, "Disabled keeping the lobby mode.", KeepFix.None);
            }

            return TryParseMode(value, current.Mode) is { } mode
                ? new KeepChange(current with { Mode = mode }, $"Keeping the lobby mode {mode.Team}, {mode.Score}", KeepFix.ModeAndSize)
                : null;
        }

        if ((match = SizeArgument().Match(text)).Success)
        {
            if (!match.Groups[1].Success
                || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var size)
                || size > MaxSize)
            {
                return null;
            }

            return size == 0
                ? new KeepChange(current with { Size = 0 }, "Disabled keeping the lobby size.", KeepFix.None)
                : new KeepChange(current with { Size = size }, string.Create(CultureInfo.InvariantCulture, $"Keeping the lobby size {size}"), KeepFix.ModeAndSize);
        }

        if ((match = ModsArgument().Match(text)).Success)
        {
            if (!match.Groups[1].Success)
            {
                return null;
            }

            if (match.Groups[1].Value.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                return new KeepChange(current with { Mods = null }, "Disabled keeping the lobby mods.", KeepFix.None);
            }

            var mods = RoomModes.ParseMods(match.Groups[1].Value);
            return new KeepChange(current with { Mods = mods }, $"Keeping the lobby mod(s) {mods.ToNames()}", KeepFix.Mods);
        }

        if ((match = PasswordArgument().Match(text)).Success)
        {
            var password = match.Groups[1].Success ? match.Groups[1].Value : string.Empty;

            // osu-ahr said the password in the lobby. This lobby only says whether there is one.
            var message = password.Length > 0 ? "Keeping the lobby password." : "Keeping the lobby without a password.";
            return new KeepChange(current with { Password = password }, message, KeepFix.Password);
        }

        if ((match = TitleArgument().Match(text)).Success)
        {
            // An empty title is not kept: Bancho would not take it.
            if (!match.Groups[2].Success)
            {
                return null;
            }

            var title = match.Groups[2].Value;
            title = title[..Math.Min(title.Length, RoomKeeping.MaxTitleLength)];
            return new KeepChange(current with { Title = title }, $"Keeping the lobby title {title}", KeepFix.Title);
        }

        return null;
    }

    private static KeepChange? Stop(RoomKeeping current, string text) => text switch
    {
        "keep mode" when current.Mode is not null =>
            new KeepChange(current with { Mode = null }, "Disabled keeping the lobby team and score modes.", KeepFix.None),
        "keep size" when current.Size != 0 =>
            new KeepChange(current with { Size = 0 }, "Disabled keeping the lobby size.", KeepFix.None),
        "keep mod" or "keep mods" when current.Mods is not null =>
            new KeepChange(current with { Mods = null }, "Disabled keeping the lobby mods.", KeepFix.None),
        "keep password" when current.Password is not null =>
            new KeepChange(current with { Password = null }, "Disabled keeping the lobby password.", KeepFix.None),
        "keep title" or "keep name" when current.Title is not null =>
            new KeepChange(current with { Title = null }, "Disabled keeping the room title.", KeepFix.None),
        _ => null,
    };

    // "team, score", "team score", a team alone or a score alone. A value left out keeps its old value.
    public static KeptMode? TryParseMode(string text, KeptMode? current)
    {
        var comma = text.Split(',');
        if (comma.Length == 2 && Both(comma[0], comma[1]) is { } byComma)
        {
            return byComma;
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 2 && Both(words[0], words[1]) is { } byWords)
        {
            return byWords;
        }

        if (RoomModes.TryParseTeamMode(text, out var team))
        {
            return new KeptMode(team, current?.Score ?? ScoreMode.Score);
        }

        if (RoomModes.TryParseScoreMode(text, out var score))
        {
            return new KeptMode(current?.Team ?? TeamMode.HeadToHead, score);
        }

        return null;

        static KeptMode? Both(string team, string score) =>
            RoomModes.TryParseTeamMode(team, out var t) && RoomModes.TryParseScoreMode(score, out var s) ? new KeptMode(t, s) : null;
    }

    [GeneratedRegex(@"^mode(?:\s+(.+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex ModeArgument();

    [GeneratedRegex(@"^size(?:\s+(.+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex SizeArgument();

    [GeneratedRegex(@"^mods?(?:\s+(.+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex ModsArgument();

    [GeneratedRegex(@"^password(?:\s+(.+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex PasswordArgument();

    [GeneratedRegex(@"^(title|name)(?:\s+(.+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex TitleArgument();
}

// Notices slots Bancho opened or locked behind the bot's back, ported from osu-ahr's SlotKeeper.
// A player in a slot past the kept size means the room grew. A player joining past an empty slot, or a
// slot inside the size left empty for 10 minutes, means a slot is locked. Either way the keeper sets
// the size again.
public sealed class SlotKeeper
{
    public static readonly TimeSpan TimeToConsiderLocked = TimeSpan.FromMinutes(10);

    private readonly (DateTimeOffset Changed, bool Taken)[] _slots = new (DateTimeOffset, bool)[Room.MaxPlayers];

    public SlotKeeper(DateTimeOffset now) => Reset(now);

    public int Size { get; set; }

    public bool Join(int slot, DateTimeOffset now)
    {
        var index = slot - 1;
        if (!InRange(index))
        {
            return false;
        }

        var drifted = Size != 0 && (Size <= index || Enumerable.Range(0, index).Any(i => !_slots[i].Taken));
        _slots[index] = (now, true);
        return drifted;
    }

    public void Leave(int slot, DateTimeOffset now)
    {
        if (InRange(slot - 1))
        {
            _slots[slot - 1] = (now, false);
        }
    }

    public bool Move(int from, int to, DateTimeOffset now)
    {
        Leave(from, now);
        var index = to - 1;
        if (!InRange(index))
        {
            return false;
        }

        _slots[index] = (now, true);
        return Size != 0 && Size <= index;
    }

    public bool HasUnusedSlot(DateTimeOffset now)
    {
        for (var i = 0; i < Size; i++)
        {
            if (!_slots[i].Taken && now - _slots[i].Changed >= TimeToConsiderLocked)
            {
                return true;
            }
        }

        return false;
    }

    // Takes the whole picture from !mp settings. True when someone sits past the kept size.
    public bool Apply(IEnumerable<int> takenSlots, DateTimeOffset now)
    {
        for (var i = 0; i < _slots.Length; i++)
        {
            _slots[i].Taken = false;
        }

        var drifted = false;
        foreach (var slot in takenSlots.Where(s => InRange(s - 1)))
        {
            _slots[slot - 1] = (now, true);
            drifted |= Size != 0 && Size < slot;
        }

        return drifted;
    }

    public void Reset(DateTimeOffset now)
    {
        for (var i = 0; i < _slots.Length; i++)
        {
            _slots[i].Changed = now;
        }
    }

    private static bool InRange(int index) => index is >= 0 and < Room.MaxPlayers;
}
