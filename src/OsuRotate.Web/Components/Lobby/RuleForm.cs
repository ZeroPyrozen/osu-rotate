using OsuRotate.Domain;
using OsuRotate.Web.Bot;

namespace OsuRotate.Web.Components.Lobby;

public enum PasswordKeeping
{
    NotKept,
    Open,
    Set,
}

// The rules as the dashboard edits them: times in seconds, keeps as switches. The password is never
// filled in from the lobby, so it never reaches the page; left empty, the kept one stays.
public sealed class RuleForm
{
    public bool RegulationEnabled { get; set; }
    public double StarMin { get; set; }
    public double StarMax { get; set; }
    public int LengthMinSeconds { get; set; }
    public int LengthMaxSeconds { get; set; }
    public GameMode Mode { get; set; }
    public bool AllowConvert { get; set; }
    public int ViolationsAllowed { get; set; }

    public bool KeepMode { get; set; }
    public TeamMode Team { get; set; }
    public ScoreMode Score { get; set; }
    public int Size { get; set; }
    public PasswordKeeping Password { get; set; }
    public string? NewPassword { get; set; }
    public bool KeepMods { get; set; }
    public string? ModsText { get; set; }
    public string? Title { get; set; }

    public double SkipShare { get; set; }
    public int SkipMinimum { get; set; }
    public double StartShare { get; set; }
    public int StartMinimum { get; set; }
    public bool StartWhenAllReady { get; set; }
    public double AbortShare { get; set; }
    public int AbortMinimum { get; set; }

    public bool SkipAfkHost { get; set; }
    public int HostAfkFirstCheckSeconds { get; set; }
    public int HostAfkCheckIntervalSeconds { get; set; }
    public bool AfkKickEnabled { get; set; }
    public int AfkKickThreshold { get; set; }
    public int AfkKickCooldownSeconds { get; set; }

    public bool AutoStartEnabled { get; set; }
    public bool AutoStartClearsHost { get; set; }
    public int AutoStartDelaySeconds { get; set; }

    public bool AbortStuckMatch { get; set; }
    public double StuckMatchFinishedShare { get; set; }
    public int StuckMatchDelaySeconds { get; set; }

    public int HostKickTolerance { get; set; }
    public int HostKickCheckIntervalSeconds { get; set; }

    public string InfoMessage { get; set; } = string.Empty;
    public int InfoAnnouncementIntervalSeconds { get; set; }

    public static RuleForm From(LobbySettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var r = s.Regulation;
        var k = s.Keeping;
        return new RuleForm
        {
            RegulationEnabled = r.Enabled,
            StarMin = r.StarMin,
            StarMax = r.StarMax,
            LengthMinSeconds = r.LengthMinSeconds,
            LengthMaxSeconds = r.LengthMaxSeconds,
            Mode = r.Mode,
            AllowConvert = r.AllowConvert,
            ViolationsAllowed = r.ViolationsAllowed,
            KeepMode = k.Mode is not null,
            Team = k.Mode?.Team ?? TeamMode.HeadToHead,
            Score = k.Mode?.Score ?? ScoreMode.Score,
            Size = k.Size,
            Password = k.Password switch
            {
                null => PasswordKeeping.NotKept,
                "" => PasswordKeeping.Open,
                _ => PasswordKeeping.Set,
            },
            KeepMods = k.Mods is not null,
            ModsText = k.Mods is { } mods ? mods.ToMpValue() : null,
            Title = k.Title,
            SkipShare = s.SkipVote.Share,
            SkipMinimum = s.SkipVote.Minimum,
            StartShare = s.StartVote.Share,
            StartMinimum = s.StartVote.Minimum,
            StartWhenAllReady = s.StartWhenAllReady,
            AbortShare = s.AbortVote.Share,
            AbortMinimum = s.AbortVote.Minimum,
            SkipAfkHost = s.SkipAfkHost,
            HostAfkFirstCheckSeconds = Seconds(s.HostAfkFirstCheck),
            HostAfkCheckIntervalSeconds = Seconds(s.HostAfkCheckInterval),
            AfkKickEnabled = s.AfkKickEnabled,
            AfkKickThreshold = s.AfkKickThreshold,
            AfkKickCooldownSeconds = Seconds(s.AfkKickCooldown),
            AutoStartEnabled = s.AutoStartEnabled,
            AutoStartClearsHost = s.AutoStartClearsHost,
            AutoStartDelaySeconds = Seconds(s.AutoStartDelay),
            AbortStuckMatch = s.AbortStuckMatch,
            StuckMatchFinishedShare = s.StuckMatchFinishedShare,
            StuckMatchDelaySeconds = Seconds(s.StuckMatchDelay),
            HostKickTolerance = s.HostKickTolerance,
            HostKickCheckIntervalSeconds = Seconds(s.HostKickCheckInterval),
            InfoMessage = s.InfoMessage,
            InfoAnnouncementIntervalSeconds = Seconds(s.InfoAnnouncementInterval),
        };
    }

    // The rules this form describes, on top of current for anything it does not show. Null with a
    // message when the form itself cannot be read; the rules are checked separately.
    public (LobbySettings? Settings, string? Problem) ToSettings(LobbySettings current)
    {
        ArgumentNullException.ThrowIfNull(current);
        Mods? mods = null;
        if (KeepMods)
        {
            var text = ModsText?.Trim() ?? string.Empty;
            mods = RoomModes.ParseMods(text);
            if (mods == Mods.None && text.Length > 0 && !text.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                return (null, "The kept mods were not recognised. Use names like HD HR, DT or Freemod, or None.");
            }
        }

        string? password;
        switch (Password)
        {
            case PasswordKeeping.Set when !string.IsNullOrEmpty(NewPassword):
                password = NewPassword;
                break;
            case PasswordKeeping.Set when current.Keeping.Password is { Length: > 0 } kept:
                password = kept;
                break;
            case PasswordKeeping.Set:
                return (null, "Enter the password to keep.");
            case PasswordKeeping.Open:
                password = string.Empty;
                break;
            default:
                password = null;
                break;
        }

        var settings = current with
        {
            Regulation = current.Regulation with
            {
                Enabled = RegulationEnabled,
                StarMin = StarMin,
                StarMax = StarMax,
                LengthMinSeconds = LengthMinSeconds,
                LengthMaxSeconds = LengthMaxSeconds,
                Mode = Mode,
                AllowConvert = AllowConvert,
                ViolationsAllowed = ViolationsAllowed,
            },
            Keeping = new RoomKeeping
            {
                Mode = KeepMode ? new KeptMode(Team, Score) : null,
                Size = Size,
                Password = password,
                Mods = mods,
                Title = string.IsNullOrWhiteSpace(Title) ? null : Title.Trim(),
            },
            SkipVote = new VoteRule(SkipShare, SkipMinimum),
            StartVote = new VoteRule(StartShare, StartMinimum),
            StartWhenAllReady = StartWhenAllReady,
            AbortVote = new VoteRule(AbortShare, AbortMinimum),
            SkipAfkHost = SkipAfkHost,
            HostAfkFirstCheck = TimeSpan.FromSeconds(HostAfkFirstCheckSeconds),
            HostAfkCheckInterval = TimeSpan.FromSeconds(HostAfkCheckIntervalSeconds),
            AfkKickEnabled = AfkKickEnabled,
            AfkKickThreshold = AfkKickThreshold,
            AfkKickCooldown = TimeSpan.FromSeconds(AfkKickCooldownSeconds),
            AutoStartEnabled = AutoStartEnabled,
            AutoStartClearsHost = AutoStartClearsHost,
            AutoStartDelay = TimeSpan.FromSeconds(AutoStartDelaySeconds),
            AbortStuckMatch = AbortStuckMatch,
            StuckMatchFinishedShare = StuckMatchFinishedShare,
            StuckMatchDelay = TimeSpan.FromSeconds(StuckMatchDelaySeconds),
            HostKickTolerance = HostKickTolerance,
            HostKickCheckInterval = TimeSpan.FromSeconds(HostKickCheckIntervalSeconds),

            // Browsers send line breaks as \r\n; the lobby splits lines on \n.
            InfoMessage = InfoMessage.Replace("\r\n", "\n", StringComparison.Ordinal).Trim(),
            InfoAnnouncementInterval = TimeSpan.FromSeconds(InfoAnnouncementIntervalSeconds),
        };
        return (settings, null);
    }

    private static int Seconds(TimeSpan span) => (int)Math.Round(span.TotalSeconds);
}

// UC8: save the edited rules. Checked here so the owner sees why; the engine checks again.
public sealed class RuleActions(LobbyRuntime runtime)
{
    public async Task<string?> SaveAsync(RuleForm form, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (runtime.Actor is null || runtime.Snapshot is not { } snapshot)
        {
            return LifecycleActions.NotRunning;
        }

        var (settings, problem) = form.ToSettings(snapshot.Settings);
        if (settings is null)
        {
            return problem;
        }

        var problems = SettingsRules.Validate(settings);
        if (problems.Count > 0)
        {
            return string.Join(' ', problems);
        }

        return await runtime.PostAsync(new Application.ChangeSettings(settings), cancellationToken).ConfigureAwait(false)
            ? null
            : LifecycleActions.NotRunning;
    }
}
