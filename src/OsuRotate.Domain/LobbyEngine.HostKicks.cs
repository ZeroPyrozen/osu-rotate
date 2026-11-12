namespace OsuRotate.Domain;

// A kick the match history shows: who was kicked, and when. The history does not say who kicked.
public sealed record SeenKick(string Username, DateTimeOffset At);

// The host-kick counterattack, ported from osu-ahr's LobbyKeeper: a host who kicks HostKickTolerance
// players in one turn, or kicks the owner or a referee, is kicked. Kicks made by the host in the game
// never show in chat, only in the match history, so the engine asks for it (ReadMatchHistory) when a
// match starts and every HostKickCheckInterval between matches.
//
// Differences from osu-ahr: kicks made by referees, the bot's own AFK kicks among them, do not count
// (osu-ahr meant to skip them but never filled the list); a kick counts against whoever hosted when it
// happened, not whoever hosts when the history is read; and kicks from before the bot joined are
// ignored.
public sealed partial class LobbyEngine
{
    private const string HostKickTimer = "host-kicks";
    private static readonly TimeSpan RefereeKickWindow = TimeSpan.FromMinutes(5);
    private const int HostHistoryLength = 64;

    private readonly List<(DateTimeOffset At, Player? Host)> _hostHistory = [];
    private readonly List<(string Key, DateTimeOffset At)> _refereeKicks = [];
    private readonly Dictionary<DateTimeOffset, HashSet<string>> _kicksByTurn = [];
    private long? _historyCursor;

    private bool HostKickCheckOn => _settings.HostKickTolerance > 0;

    private void HostKicksOnRoomEntered()
    {
        _historyCursor = null;
        if (HostKickCheckOn)
        {
            // The first read only finds where the history ends; older kicks are not judged.
            _output.Add(new ReadMatchHistory(null));
            ScheduleHostKickCheck();
        }
    }

    private void HostKicksOnHostChanged(Player? host)
    {
        _hostHistory.Add((_time.GetUtcNow(), host));
        if (_hostHistory.Count > HostHistoryLength)
        {
            _kicksByTurn.Remove(_hostHistory[0].At);
            _hostHistory.RemoveAt(0);
        }
    }

    private void HostKicksOnMatchStarted()
    {
        if (HostKickCheckOn && _historyCursor is not null)
        {
            _output.Add(new ReadMatchHistory(_historyCursor));
        }
    }

    // "Kicked X from the match." answers a referee's !mp kick: the bot's own, or another referee's.
    private void HostKicksOnRefereeKick(string username)
    {
        var now = _time.GetUtcNow();
        _refereeKicks.RemoveAll(k => now - k.At > RefereeKickWindow);
        _refereeKicks.Add((Player.KeyOf(username), now));
    }

    private void ScheduleHostKickCheck()
    {
        if (HostKickCheckOn && _settings.HostKickCheckInterval > TimeSpan.Zero)
        {
            Timers.Start(HostKickTimer, _settings.HostKickCheckInterval, OnHostKickTimer);
        }
    }

    // Until a first read has come back (the API may have been down), this asks again from the start.
    private void OnHostKickTimer()
    {
        if (!Room.IsMatching)
        {
            _output.Add(new ReadMatchHistory(_historyCursor));
        }

        ScheduleHostKickCheck();
    }

    private void HostKicksOnHistoryRead(MatchHistoryRead e)
    {
        var first = _historyCursor is null;
        if (_historyCursor is { } cursor && e.LatestEventId <= cursor)
        {
            return;
        }

        _historyCursor = e.LatestEventId;
        if (first || !HostKickCheckOn)
        {
            return;
        }

        foreach (var kick in e.Kicks.OrderBy(k => k.At))
        {
            JudgeKick(kick);
        }
    }

    private void JudgeKick(SeenKick kick)
    {
        var key = Player.KeyOf(kick.Username);
        var byReferee = _refereeKicks.FindIndex(k => k.Key == key && (kick.At - k.At).Duration() <= RefereeKickWindow);
        if (byReferee >= 0)
        {
            _refereeKicks.RemoveAt(byReferee);
            return;
        }

        // Whoever hosted when the kick happened.
        var turn = _hostHistory.LastOrDefault(h => h.At <= kick.At);
        if (turn.Host is not { } host || IsOwner(host.Name) || Room.IsReferee(host.Name))
        {
            return;
        }

        if (!_kicksByTurn.TryGetValue(turn.At, out var kicked))
        {
            _kicksByTurn[turn.At] = kicked = [];
        }

        kicked.Add(key);
        var victimIsPrivileged = IsOwner(kick.Username) || Room.IsReferee(kick.Username);
        if (kicked.Count >= _settings.HostKickTolerance || victimIsPrivileged)
        {
            kicked.Clear();
            if (Room.Contains(host))
            {
                _output.Add(new KickPlayer(host.Name));
            }
        }
    }
}
