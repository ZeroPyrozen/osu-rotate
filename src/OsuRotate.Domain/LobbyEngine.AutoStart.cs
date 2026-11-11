namespace OsuRotate.Domain;

// Auto start, ported from osu-ahr's AutoStartTimer: when the host picks a new map, queue a start.
// Stuck-match abort, ported from MatchAborter: once half the players have finished, wait 30 seconds,
// then ask BanchoBot about everyone still playing. If none of them is really in a match, abort.
public sealed partial class LobbyEngine
{
    public static readonly TimeSpan MinimumAutoStartDelay = TimeSpan.FromSeconds(15);

    private const string StuckMatchTimer = "stuck-match";
    private const string StuckMatchReplyTimer = "stuck-match-reply";

    private readonly HashSet<Player> _stuckMatchCheck = [];
    private int _lastPlayedBeatmapId;

    public bool IsStuckMatchCheckRunning => Timers.IsRunning(StuckMatchTimer) || _stuckMatchCheck.Count > 0;

    private bool AutoStartActive => _settings.AutoStartEnabled && _settings.AutoStartDelay >= MinimumAutoStartDelay;

    private void AutoStartOnBeatmapChanging()
    {
        if (AutoStartActive)
        {
            StopStartTimer();
        }
    }

    private void AutoStartOnBeatmapChanged(int beatmapId)
    {
        // A lone player starts when they like, and the map just played does not start itself again.
        if (!AutoStartActive || Room.Players.Count == 1 || beatmapId == _lastPlayedBeatmapId)
        {
            return;
        }

        StartMatchIn((int)_settings.AutoStartDelay.TotalSeconds, withHint: true);
        if (_settings.AutoStartClearsHost)
        {
            _output.Add(new ClearHost());
        }
    }

    private void CheckStuckMatch()
    {
        if (_settings.StuckMatchDelay <= TimeSpan.Zero || !Room.IsMatching || IsStuckMatchCheckRunning)
        {
            return;
        }

        var inGame = Room.Players.Count(p => p.Status is MatchStatus.Playing or MatchStatus.Finished);
        var finished = Room.Players.Count(p => p.Status == MatchStatus.Finished);
        if (finished >= Math.Ceiling(inGame * _settings.StuckMatchFinishedShare))
        {
            Timers.Start(StuckMatchTimer, _settings.StuckMatchDelay, OnStuckMatchTimer);
        }
    }

    private void OnStuckMatchTimer()
    {
        if (!Room.IsMatching)
        {
            return;
        }

        foreach (var player in Room.Players.Where(p => p.Status == MatchStatus.Playing))
        {
            _stuckMatchCheck.Add(player);
            _output.Add(new RequestStat(player.Name, ViaPrivateMessage: true));
        }

        if (_stuckMatchCheck.Count == 0)
        {
            OnMatchStuck();
            return;
        }

        // No answer counts as not playing, as in osu-ahr.
        Timers.Start(StuckMatchReplyTimer, _settings.StatReplyTimeout, OnMatchStuck);
    }

    private void StuckMatchOnStat(PlayerStat stat)
    {
        if (Room.Find(stat.Username) is not { } player || !_stuckMatchCheck.Remove(player))
        {
            return;
        }

        if (stat.Activity == PlayerActivity.Multiplaying)
        {
            // Someone is still playing, so the match is alive. Look again later.
            StopStuckMatchCheck();
            Timers.Start(StuckMatchTimer, _settings.StuckMatchDelay, OnStuckMatchTimer);
        }
        else if (_stuckMatchCheck.Count == 0)
        {
            OnMatchStuck();
        }
    }

    private void OnMatchStuck()
    {
        StopStuckMatchCheck();
        if (!Room.IsMatching)
        {
            return;
        }

        if (_settings.AbortStuckMatch)
        {
            AbortMatchNow();
        }
        else
        {
            Announce("If the match is stuck, abort the match with !abort to vote.");
        }
    }

    private void StopStuckMatchCheck()
    {
        _stuckMatchCheck.Clear();
        Timers.Cancel(StuckMatchTimer);
        Timers.Cancel(StuckMatchReplyTimer);
    }
}
