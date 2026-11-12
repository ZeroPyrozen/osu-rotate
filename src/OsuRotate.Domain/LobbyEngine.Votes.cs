using System.Globalization;
using System.Text.RegularExpressions;

namespace OsuRotate.Domain;

// Skip, start and abort votes, ported from osu-ahr's HostSkipper, MatchStarter and MatchAborter.
// The host's own !skip, !start or !abort acts at once; other players vote. Progress messages wait a
// few seconds so a burst of votes produces one message.
public sealed partial class LobbyEngine
{
    private const string SkipVoteKey = "skip-vote";
    private const string StartVoteKey = "start-vote";
    private const string AbortVoteKey = "abort-vote";
    private const string MatchStartTimer = "match-start";
    private const string MatchStart10SecondsKey = "match-start-10s";
    private const string MatchStart30SecondsKey = "match-start-30s";
    private const string StartMatchKey = "start-match";
    private static readonly TimeSpan PassedMessageDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan StartMatchCooldown = TimeSpan.FromSeconds(1);

    private DateTimeOffset _hostChangedAt = DateTimeOffset.MinValue;
    private bool _countdownRunning;

    public VoteCounter SkipVotes { get; }
    public VoteCounter StartVotes { get; }
    public VoteCounter AbortVotes { get; }

    // True while the bot's own !start countdown runs, as opposed to one started with !mp start N.
    public bool IsStartTimerRunning => Timers.IsRunning(MatchStartTimer);

    private void VotesOnPlayerJoined(Player player)
    {
        SkipVotes.AddVoter(player);
        StartVotes.AddVoter(player);
    }

    private void VotesOnPlayerLeft(Player player, bool wasHost)
    {
        SkipVotes.RemoveVoter(player);
        StartVotes.RemoveVoter(player);
        AbortVotes.RemoveVoter(player);

        // Fewer players can need fewer votes, so a vote may now pass.
        if (Room.IsMatching)
        {
            CheckAbortVotes(showProgress: false);
            return;
        }

        // The host is already gone, so passing a skip vote now would skip the next host as well.
        // osu-ahr skipped twice here.
        if (wasHost)
        {
            SkipVotes.Clear();
            CancelDeferred(SkipVoteKey);
        }
        else
        {
            CheckSkipVotes(showProgress: false);
        }

        CheckStartVotes(showProgress: false);
        if (Room.Players.Count == 0)
        {
            StopStartTimer();
        }
    }

    private void VotesOnHostChanged()
    {
        SkipVotes.Clear();
        _hostChangedAt = _time.GetUtcNow();
        if (!Room.IsMatching)
        {
            StartVotes.Clear();
            StopStartTimer();
        }
    }

    private void VotesOnMatchStarted()
    {
        _countdownRunning = false;
        SkipVotes.Clear();
        StartVotes.Clear();
        StopStartTimer();
        AbortVotes.SetVoters(Room.Players);
    }

    private void OnSkipCommand(Player? player, string argument)
    {
        if (player is null || Room.IsMatching || SkipVotes.Passed)
        {
            return;
        }

        // "!skip name" only counts against the current host.
        if (argument.Length > 0 && Room.Host is { } host && Player.KeyOf(argument) != host.Key)
        {
            return;
        }

        // Votes right after a host change were probably meant for the previous host.
        var sinceHostChanged = _time.GetUtcNow() - _hostChangedAt;
        if (sinceHostChanged < _settings.SkipVoteCooldown)
        {
            if (player == Room.Host)
            {
                var wait = (_settings.SkipVoteCooldown - sinceHostChanged).TotalSeconds;
                Announce($"The host skip command is currently in cooltime. You have to wait {wait.ToString("0.00", CultureInfo.InvariantCulture)} sec(s).");
            }

            return;
        }

        if (player == Room.Host)
        {
            SkipHost();
        }
        else if (SkipVotes.Vote(player))
        {
            CheckSkipVotes(showProgress: true);
        }
    }

    private void CheckSkipVotes(bool showProgress)
    {
        if (SkipVotes.Count > 0 && showProgress)
        {
            Defer(SkipVoteKey, $"Host skip progress: {SkipVotes}", _settings.VoteMessageDelay, restart: false);
        }

        if (SkipVotes.Passed)
        {
            Defer(SkipVoteKey, $"Passed a host skip vote: {SkipVotes}", PassedMessageDelay, restart: true);
            SkipHost();
        }
    }

    private void SkipHost()
    {
        StopHostAfkCheck();
        _hostChangedAt = _time.GetUtcNow();
        Rotation.Skip();
    }

    private void OnStartCommand(Player? player, bool isOwner, string argument)
    {
        if (Room.IsMatching)
        {
            return;
        }

        var isHost = player is not null && player == Room.Host;
        if (argument.Length == 0)
        {
            if (isHost)
            {
                StartMatchNow();
            }
            else if (player is not null && !StartVotes.Passed && StartVotes.Vote(player))
            {
                CheckStartVotes(showProgress: true);
            }
        }
        else if ((isHost || isOwner) && Seconds().IsMatch(argument) && int.TryParse(argument, CultureInfo.InvariantCulture, out var seconds))
        {
            StartMatchIn(seconds);
        }
    }

    // !stop, or !abort outside a match, cancels the bot's own start countdown.
    private void OnStopCommand(Player? player, bool isOwner)
    {
        var isHost = player is not null && player == Room.Host;
        if (Room.IsMatching || !(isHost || isOwner) || !IsStartTimerRunning)
        {
            return;
        }

        Announce("Aborted the match start timer.");
        StopStartTimer();
    }

    private void CheckStartVotes(bool showProgress)
    {
        if (StartVotes.Count > 0 && showProgress)
        {
            Defer(StartVoteKey, $"Match start progress: {StartVotes}", _settings.VoteMessageDelay, restart: false);
        }

        if (StartVotes.Passed)
        {
            Defer(StartVoteKey, $"Passed a match start vote: {StartVotes}", PassedMessageDelay, restart: true);
            StartMatchNow();
        }
    }

    private void StartMatchNow()
    {
        StopStartTimer();
        if (CooledDown(StartMatchKey, StartMatchCooldown))
        {
            _output.Add(new StartMatch());
        }

        StartVotes.Clear();
    }

    private void StartMatchIn(int seconds, bool withHint = false)
    {
        if (seconds == 0)
        {
            StartMatchNow();
            return;
        }

        CancelOwnStartTimers();
        Announce($"Queued the match to start in {CountdownText(seconds)}{(withHint ? ". (Host can stop the timer with !stop command.)" : string.Empty)}");
        Timers.Start(MatchStartTimer, TimeSpan.FromSeconds(seconds), StartMatchNow);
        if (seconds > 15)
        {
            Defer(MatchStart10SecondsKey, "Match starts in 10 seconds", TimeSpan.FromSeconds(seconds - 10), restart: true);
        }

        if (seconds > 35)
        {
            Defer(MatchStart30SecondsKey, "Match starts in 30 seconds", TimeSpan.FromSeconds(seconds - 30), restart: true);
        }
    }

    private void StopStartTimer()
    {
        CancelOwnStartTimers();
        CancelDeferred(StartVoteKey);
        if (_countdownRunning)
        {
            _countdownRunning = false;
            _output.Add(new AbortCountdown());
        }
    }

    private void CancelOwnStartTimers()
    {
        Timers.Cancel(MatchStartTimer);
        CancelDeferred(MatchStart10SecondsKey);
        CancelDeferred(MatchStart30SecondsKey);
    }

    private void OnAbortVoteCommand(Player? player)
    {
        if (player is null || !Room.IsMatching)
        {
            return;
        }

        if (player == Room.Host)
        {
            AbortMatchNow();
        }
        else if (!AbortVotes.Passed && AbortVotes.Vote(player))
        {
            CheckAbortVotes(showProgress: true);
        }
    }

    private void CheckAbortVotes(bool showProgress)
    {
        if (AbortVotes.Count > 0 && showProgress)
        {
            Defer(AbortVoteKey, $"Match abort progress: {AbortVotes}", _settings.VoteMessageDelay, restart: false);
        }

        if (AbortVotes.Passed)
        {
            Defer(AbortVoteKey, $"Passed a match abort vote: {AbortVotes}", PassedMessageDelay, restart: true);
            AbortMatchNow();
        }
    }

    private void AbortMatchNow()
    {
        StopStuckMatchCheck();
        _output.Add(new AbortMatch());
    }

    public static string CountdownText(int seconds)
    {
        var minutes = seconds / 60;
        var rest = seconds % 60;
        var minuteText = minutes switch { 0 => string.Empty, 1 => "1 minute", _ => $"{minutes} minutes" };
        var secondText = rest switch { 0 => string.Empty, 1 => "1 second", _ => $"{rest} seconds" };
        var and = minutes > 0 && rest > 0 ? " and " : string.Empty;
        return $"{minuteText}{and}{secondText}";
    }

    [GeneratedRegex(@"^[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Seconds();
}
