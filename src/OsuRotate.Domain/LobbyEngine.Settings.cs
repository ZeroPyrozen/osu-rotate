using System.Globalization;

namespace OsuRotate.Domain;

// The owner editing the rules from the dashboard (UC8). The effects match the chat commands: a changed
// keep is put right in the room at once, and a changed regulation is announced.
public sealed partial class LobbyEngine
{
    // Refused settings change nothing. The owner and the running build are not the dashboard's to set.
    public IReadOnlyList<LobbyCommand> ChangeSettings(LobbySettings next)
    {
        next = next with { OwnerName = _settings.OwnerName, BotVersion = _settings.BotVersion };
        if (SettingsRules.Validate(next).Count > 0 || next == _settings)
        {
            return Flush();
        }

        var before = _settings;
        UpdateSettings(next);
        FixChangedKeeping(before.Keeping, next.Keeping);
        AnnounceRegulationChange(before.Regulation, next.Regulation);
        return Flush();
    }

    private void FixChangedKeeping(RoomKeeping before, RoomKeeping after)
    {
        // Sends nothing when neither is kept any more.
        if (before.Mode != after.Mode || before.Size != after.Size)
        {
            FixModeAndSize();
        }

        if (before.Mods != after.Mods)
        {
            FixMods();
        }

        if (before.Password != after.Password)
        {
            if (after.Password is null)
            {
                // No longer keeping a password: open the room rather than leave the old one behind.
                if (before.Password is { Length: > 0 })
                {
                    _output.Add(new SetPassword(null));
                }
            }
            else
            {
                FixPassword();
            }
        }

        if (before.Title != after.Title)
        {
            FixTitle();
        }
    }

    private void AnnounceRegulationChange(MapRegulation before, MapRegulation after)
    {
        if (after.Enabled != before.Enabled)
        {
            Announce(after.Enabled ? "Map Checker plugin enabled." : "Map Checker plugin disabled.");
        }

        if ((after with { Enabled = before.Enabled, ViolationsAllowed = before.ViolationsAllowed, DescriptionTemplate = before.DescriptionTemplate }) != before)
        {
            Announce($"New regulation: {after.Describe()}");
        }
    }
}

// What the dashboard may set. Ported from osu-ahr's option validation, with the limits the chat
// commands already apply. Each problem is one sentence for the owner.
public static class SettingsRules
{
    public const int MaxStarRating = 20;
    public const int MaxLengthSeconds = 60 * 60;
    public const int MaxInfoMessageLength = 1000;
    public const int MaxChatLineLength = 400;
    public const int MaxPasswordLength = 50;
    public static readonly TimeSpan MinimumAfkKickCooldown = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan MinimumHostKickCheckInterval = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan MaximumDelay = TimeSpan.FromHours(1);

    public static IReadOnlyList<string> Validate(LobbySettings settings)
    {
        var problems = new List<string>();
        Regulation(settings.Regulation, problems);
        Keeping(settings.Keeping, problems);
        Vote("skip", settings.SkipVote, problems);
        Vote("start", settings.StartVote, problems);
        Vote("abort", settings.AbortVote, problems);

        Delay("The first host AFK check", settings.HostAfkFirstCheck, TimeSpan.Zero, problems);
        Delay("The host AFK check interval", settings.HostAfkCheckInterval, TimeSpan.Zero, problems);
        if (settings.AfkKickThreshold < 1)
        {
            problems.Add("The AFK kick threshold must be at least 1.");
        }

        Delay("The AFK kick cooldown", settings.AfkKickCooldown, MinimumAfkKickCooldown, problems);
        Delay("The auto start delay", settings.AutoStartDelay, LobbyEngine.MinimumAutoStartDelay, problems);

        if (!Share(settings.StuckMatchFinishedShare))
        {
            problems.Add("The share of finished players for a stuck match must be from 0 to 1.");
        }

        Delay("The stuck match delay", settings.StuckMatchDelay, TimeSpan.Zero, problems);

        if (settings.HostKickTolerance < 0)
        {
            problems.Add("The host-kick tolerance must be 0 (off) or more.");
        }

        Delay("The host-kick check interval", settings.HostKickCheckInterval, MinimumHostKickCheckInterval, problems);

        InfoMessage(settings.InfoMessage, problems);
        if (settings.InfoAnnouncementInterval != TimeSpan.Zero)
        {
            Delay("The info announcement interval", settings.InfoAnnouncementInterval, LobbyEngine.MinimumInfoAnnouncementInterval, problems, "or 0 (off)");
        }

        return problems;
    }

    private static void Regulation(MapRegulation regulation, List<string> problems)
    {
        Range("star rating", regulation.StarMin, regulation.StarMax, MaxStarRating, v => v.ToString("0.00", CultureInfo.InvariantCulture), problems);
        Range("length in seconds", regulation.LengthMinSeconds, regulation.LengthMaxSeconds, MaxLengthSeconds, v => v.ToString("0", CultureInfo.InvariantCulture), problems);
        if (!Enum.IsDefined(regulation.Mode))
        {
            problems.Add("The game mode must be osu!, osu!taiko, osu!catch or osu!mania.");
        }

        if (regulation.ViolationsAllowed < 0)
        {
            problems.Add("The violations allowed must be 0 (never skip) or more.");
        }

        if (string.IsNullOrWhiteSpace(regulation.DescriptionTemplate) || HasLineBreak(regulation.DescriptionTemplate))
        {
            problems.Add("The map description must be one line of text.");
        }
    }

    private static void Range(string name, double min, double max, double limit, Func<double, string> format, List<string> problems)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max) || min < 0 || max < 0 || min > limit || max > limit)
        {
            problems.Add($"The {name} limits must be from 0 (no limit) to {format(limit)}.");
        }
        else if (max > 0 && max < min)
        {
            problems.Add($"The highest {name} must not be below the lowest.");
        }
    }

    private static void Keeping(RoomKeeping keeping, List<string> problems)
    {
        if (keeping.Mode is { } mode && (!Enum.IsDefined(mode.Team) || !Enum.IsDefined(mode.Score)))
        {
            problems.Add("The kept team and score modes are not ones Bancho knows.");
        }

        if (keeping.Size is < 0 or > KeepCommand.MaxSize)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"The kept size must be from 0 (not kept) to {KeepCommand.MaxSize}."));
        }

        if (keeping.Mods is { } mods && RoomModes.RemoveInvalidCombinations(mods) != mods)
        {
            problems.Add("The kept mods cannot be played together.");
        }

        if (keeping.Password is { } password
            && (password.Length > MaxPasswordLength || password.Any(char.IsWhiteSpace)))
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"The kept password must have no spaces and at most {MaxPasswordLength} characters."));
        }

        if (keeping.Title is { } title
            && (string.IsNullOrWhiteSpace(title) || title.Length > RoomKeeping.MaxTitleLength || HasLineBreak(title)))
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"The kept title must be one line of 1 to {RoomKeeping.MaxTitleLength} characters."));
        }
    }

    private static void Vote(string name, VoteRule rule, List<string> problems)
    {
        if (!Share(rule.Share) || rule.Minimum < 1)
        {
            problems.Add($"The {name} vote needs a share from 0 to 1 and at least 1 vote.");
        }
    }

    private static void Delay(string name, TimeSpan delay, TimeSpan minimum, List<string> problems, string? alternative = null)
    {
        if (delay < minimum || delay > MaximumDelay)
        {
            var from = minimum.TotalSeconds.ToString("0", CultureInfo.InvariantCulture);
            var to = MaximumDelay.TotalSeconds.ToString("0", CultureInfo.InvariantCulture);
            var or = alternative is null ? string.Empty : $" {alternative}";
            problems.Add($"{name} must be from {from} to {to} seconds{or}.");
        }
    }

    private static void InfoMessage(string message, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > MaxInfoMessageLength)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"The info message must have 1 to {MaxInfoMessageLength} characters."));
        }
        else if (message.Split('\n').Any(line => line.Length > MaxChatLineLength || line.Contains('\r', StringComparison.Ordinal)))
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"Each line of the info message must have at most {MaxChatLineLength} characters."));
        }
    }

    private static bool Share(double share) => double.IsFinite(share) && share is >= 0 and <= 1;

    private static bool HasLineBreak(string text) => text.Contains('\n', StringComparison.Ordinal) || text.Contains('\r', StringComparison.Ordinal);
}
