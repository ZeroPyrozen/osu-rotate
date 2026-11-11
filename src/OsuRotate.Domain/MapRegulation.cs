using System.Globalization;
using System.Text.RegularExpressions;

namespace OsuRotate.Domain;

// The value is what !mp map and the osu! API use.
public enum GameMode
{
    Osu = 0,
    Taiko = 1,
    Catch = 2,
    Mania = 3,
}

public static class GameModes
{
    public static string OfficialName(this GameMode mode) => mode switch
    {
        GameMode.Taiko => "osu!taiko",
        GameMode.Catch => "osu!catch",
        GameMode.Mania => "osu!mania",
        _ => "osu!",
    };

    // Accepts the names osu-ahr accepted, plus the numbers 0 to 3.
    public static bool TryParse(string text, out GameMode mode)
    {
        mode = text.Trim().ToLowerInvariant() switch
        {
            "osu" or "0" or "osu!" or "standard" or "std" => GameMode.Osu,
            "taiko" or "1" => GameMode.Taiko,
            "fruits" or "catch" or "catchthebeat" or "ctb" or "2" => GameMode.Catch,
            "mania" or "osumania" or "3" => GameMode.Mania,
            _ => (GameMode)(-1),
        };
        return Enum.IsDefined(mode);
    }
}

// A beatmap as it would be played in the lobby's mode, so Mode differs from the regulation's only
// when the catalog could not find a version in that mode.
public sealed record BeatmapInfo(
    int Id,
    int BeatmapsetId,
    string Title,
    GameMode Mode,
    double StarRating,
    int LengthSeconds,
    string Status);

public enum BeatmapLookupOutcome
{
    Found,
    NotFound,
    ModeMismatch,
    NotAvailable,

    // The catalog could not answer, for example because the osu! API was down. The map is let through.
    Failed,
}

public sealed record BeatmapLookup(BeatmapLookupOutcome Outcome, BeatmapInfo? Beatmap = null)
{
    public static BeatmapLookup Found(BeatmapInfo beatmap) => new(BeatmapLookupOutcome.Found, beatmap);
}

// Which maps a host may pick. Ported from osu-ahr's MapChecker and MapValidator. A limit of 0 means none.
public sealed record MapRegulation
{
    public const string DefaultDescriptionTemplate =
        "[https://osu.ppy.sh/b/${map_id} ${title}] | Star Rating: ${star} | Duration: ${length} | Status: ${status} | Alternative: [https://beatconnect.io/b/${beatmapset_id} (BeatConnect.io)] | [https://osu.direct/d/${beatmapset_id} (osu.direct)]";

    public bool Enabled { get; init; } = true;
    public double StarMin { get; init; }
    public double StarMax { get; init; }
    public int LengthMinSeconds { get; init; }
    public int LengthMaxSeconds { get; init; }
    public GameMode Mode { get; init; } = GameMode.Osu;
    public bool AllowConvert { get; init; } = true;

    // Rejected picks a host may make before losing the turn. 0 never skips.
    public int ViolationsAllowed { get; init; } = 3;
    public string DescriptionTemplate { get; init; } = DefaultDescriptionTemplate;

    // How far outside the limits a map is, and why. Star differences are rounded to 2 places as in
    // osu-ahr, so a map within 0.005 stars of a limit passes.
    public (double Rate, string Message) Rate(BeatmapInfo map)
    {
        var rate = 0.0;
        var reasons = new List<string>();
        if (map.Mode != Mode)
        {
            rate += 1;
            reasons.Add($"the gamemode is not {Mode.OfficialName()}.");
        }

        if (StarMin > 0 && map.StarRating < StarMin)
        {
            rate += Math.Round(StarMin - map.StarRating, 2);
            reasons.Add("the beatmap star rating is lower than the allowed star rating.");
        }

        if (StarMax > 0 && StarMax < map.StarRating)
        {
            rate += Math.Round(map.StarRating - StarMax, 2);
            reasons.Add("the beatmap star rating is higher than the allowed star rating.");
        }

        if (LengthMinSeconds > 0 && map.LengthSeconds < LengthMinSeconds)
        {
            rate += (LengthMinSeconds - map.LengthSeconds) / 60.0;
            reasons.Add("the beatmap length is shorter than the allowed length.");
        }

        if (LengthMaxSeconds > 0 && LengthMaxSeconds < map.LengthSeconds)
        {
            rate += (map.LengthSeconds - LengthMaxSeconds) / 60.0;
            reasons.Add("the beatmap length is longer than the allowed length.");
        }

        if (rate <= 0)
        {
            return (0, string.Empty);
        }

        var star = map.StarRating.ToString(CultureInfo.InvariantCulture);
        var description = $"[https://osu.ppy.sh/b/{map.Id} {map.Title}] (Star rating: {star}, Length: {Duration(map.LengthSeconds)})";
        var message = reasons.Count == 1
            ? $"{description} was rejected because {reasons[0]}"
            : $"{description} was rejected because of following reason:\n{string.Join('\n', reasons.Select(r => $"- {r}"))}";
        return (rate, message);
    }

    public string Describe()
    {
        var parts = new List<string>();
        var star = Range(StarMin, StarMax, "star rating", v => v.ToString("0.00", CultureInfo.InvariantCulture));
        if (star.Length > 0)
        {
            parts.Add(star);
        }

        var length = Range(LengthMinSeconds, LengthMaxSeconds, "length", v => Duration((int)v));
        if (length.Length > 0)
        {
            parts.Add(length);
        }

        var mode = $"Mode: {Mode.OfficialName()}";
        if (Mode != GameMode.Osu)
        {
            mode += AllowConvert ? " (Converts allowed)" : " (Converts disallowed)";
        }

        parts.Add(mode);
        var text = string.Join(", ", parts);
        return Enabled ? text : $"Disabled ({text})";
    }

    public string DescribeMap(BeatmapInfo map) => DescriptionTemplate
        .Replace("${title}", map.Title, StringComparison.Ordinal)
        .Replace("${map_id}", map.Id.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("${beatmapset_id}", map.BeatmapsetId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("${star}", map.StarRating.ToString("0.00", CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("${length}", Duration(map.LengthSeconds), StringComparison.Ordinal)
        .Replace("${status}", map.Status, StringComparison.Ordinal);

    public static string Duration(int seconds) => $"{seconds / 60}:{seconds % 60:00}";

    private static string Range(double min, double max, string name, Func<double, string> format) => (min > 0, max > 0) switch
    {
        (true, true) => $"{format(min)} <= {name} <= {format(max)}",
        (true, false) => $"{format(min)} <= {name}",
        (false, true) => $"{name} <= {format(max)}",
        _ => string.Empty,
    };
}

// The owner's *regulation, *no regulation and *mapchecker_enable/disable commands, ported from
// osu-ahr's parseMapcheckerOwnerCommand. Returns null when the command changes nothing or is invalid.
public static partial class RegulationCommand
{
    public static MapRegulation? Apply(MapRegulation current, string command, string argument)
    {
        argument = argument.Trim();
        switch (command.ToLowerInvariant())
        {
            case "*mapchecker_enable":
                return current with { Enabled = true };
            case "*mapchecker_disable":
                return current with { Enabled = false };
            case "*no" when argument.StartsWith("regulation", StringComparison.OrdinalIgnoreCase):
                var words = Words(argument);
                return words.Length == 1 ? current with { Enabled = false } : ApplyNo(current, words[1]);
            case "*regulation" when argument.Contains('=', StringComparison.Ordinal):
                var next = current;
                foreach (Match pair in Setter().Matches(argument))
                {
                    if (Set(next, Unify(pair.Groups[1].Value), pair.Groups[2].Value) is not { } changed)
                    {
                        return null;
                    }

                    next = changed;
                }

                return next == current ? null : next;
            case "*regulation":
                var parts = Words(argument);
                return parts.Length == 0 ? null : Set(current, Unify(parts[0]), parts.Length > 1 ? parts[1] : null);
            default:
                return null;
        }
    }

    private static MapRegulation? ApplyNo(MapRegulation current, string name) => Unify(name) switch
    {
        "num_violations_allowed" => current with { ViolationsAllowed = 0 },
        "star_min" => current with { StarMin = 0 },
        "star_max" => current with { StarMax = 0 },
        "length_min" => current with { LengthMinSeconds = 0 },
        "length_max" => current with { LengthMaxSeconds = 0 },
        "gamemode" => current with { Mode = GameMode.Osu, AllowConvert = true },
        "allow_convert" => current with { AllowConvert = false },
        _ => null,
    };

    private static MapRegulation? Set(MapRegulation current, string name, string? value)
    {
        switch (name)
        {
            case "enabled":
                return current with { Enabled = true };
            case "disabled":
                return current with { Enabled = false };
            case "allow_convert" when value is null:
                return current with { AllowConvert = true };
            case "allow_convert":
                return bool.TryParse(value, out var allow) ? current with { AllowConvert = allow } : null;
            case "disallow_convert":
                return current with { AllowConvert = false };
            case "gamemode":
                return value is not null && GameModes.TryParse(value, out var mode) ? current with { Mode = mode } : null;
        }

        if (value is null || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            || number < 0 || double.IsNaN(number) || double.IsInfinity(number))
        {
            return null;
        }

        // A new limit that crosses the other one clears the other one.
        return name switch
        {
            "num_violations_allowed" => current with { ViolationsAllowed = (int)number },
            "star_min" => current with { StarMin = number, StarMax = Crosses(current.StarMax, number) ? 0 : current.StarMax },
            "star_max" => current with { StarMax = number, StarMin = Crosses(number, current.StarMin) ? 0 : current.StarMin },
            "length_min" => current with { LengthMinSeconds = (int)number, LengthMaxSeconds = Crosses(current.LengthMaxSeconds, number) ? 0 : current.LengthMaxSeconds },
            "length_max" => current with { LengthMaxSeconds = (int)number, LengthMinSeconds = Crosses(number, current.LengthMinSeconds) ? 0 : current.LengthMinSeconds },
            _ => null,
        };
    }

    private static bool Crosses(double max, double min) => max > 0 && max <= min;

    private static string[] Words(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    // osu-ahr's unifyParamName: "starmin", "difflow", "lenupperlimit" and similar all work.
    private static string Unify(string name)
    {
        name = name.ToLowerInvariant();
        if (name.Contains("star", StringComparison.Ordinal) || name.Contains("diff", StringComparison.Ordinal))
        {
            return LowOrHigh(name, "star_min", "star_max");
        }

        if (name.Contains("len", StringComparison.Ordinal))
        {
            return LowOrHigh(name, "length_min", "length_max");
        }

        if (name.StartsWith("enable", StringComparison.Ordinal))
        {
            return "enabled";
        }

        if (name.StartsWith("disable", StringComparison.Ordinal))
        {
            return "disabled";
        }

        if (name.Contains("violations", StringComparison.Ordinal))
        {
            return "num_violations_allowed";
        }

        return name switch
        {
            "allowconvert" => "allow_convert",
            "disallowconvert" => "disallow_convert",
            _ => name,
        };
    }

    private static string LowOrHigh(string name, string low, string high)
    {
        if (name.Contains("low", StringComparison.Ordinal) || name.Contains("min", StringComparison.Ordinal))
        {
            return low;
        }

        return name.Contains("up", StringComparison.Ordinal) || name.Contains("max", StringComparison.Ordinal) ? high : name;
    }

    [GeneratedRegex(@"([0-9a-zA-Z_-]+)\s*=\s*([^\s,]+)", RegexOptions.CultureInvariant)]
    private static partial Regex Setter();
}
