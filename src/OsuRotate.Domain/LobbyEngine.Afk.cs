namespace OsuRotate.Domain;

// AFK handling, ported from osu-ahr's HostSkipper AFK check and AfkKicker.
//
// Host check: while the host has not started a match, the bot quietly asks BanchoBot for the host's
// !stat by private message every 90 seconds (45 for a new host). If the host looks away, it asks again
// in the room, so players see why, and that public answer skips the host. Anyone typing !stat for an
// away host in the room has the same effect.
//
// Kicker: players collect points for looking away (3 per away !stat, 2 per match finished without a
// score or without the map) and lose them by chatting (1) or scoring (all). At 6 points they are kicked.
// Points change at most once per 30 seconds per player. The owner is never kicked, because the bot
// runs on the owner's account.
public sealed partial class LobbyEngine
{
    private const string HostAfkTimer = "host-afk";
    private const string HostAfkReplyTimer = "host-afk-reply";
    private const int PointsAwayStat = 3;
    private const int PointsNoScore = 2;
    private const int PointsNoMap = 2;
    private const int PointsScored = -100;
    private const int PointsChat = -1;

    private readonly Dictionary<Player, AfkRecord> _afk = [];
    private Player? _hostAfkTarget;
    private bool _mapChangedSinceMatch;

    public int AfkPoints(string name) =>
        Room.Find(name) is { } player && _afk.TryGetValue(player, out var record) ? record.Points : 0;

    public bool IsHostAfkCheckRunning => Timers.IsRunning(HostAfkTimer) || Timers.IsRunning(HostAfkReplyTimer);

    // Unknown and None are not evidence of anything, so they never count as away.
    private static bool IsAway(PlayerActivity activity) =>
        activity is not (PlayerActivity.Multiplayer or PlayerActivity.Multiplaying or PlayerActivity.Unknown or PlayerActivity.None);

    private void AfkOnPlayerJoined(Player player)
    {
        _afk[player] = new AfkRecord(_time.GetUtcNow());

        // Someone joined a lone host who was recently seen away: hand the room to the newcomer.
        if (Room.Players.Count == 2
            && Room.Host is { } host
            && _afk.TryGetValue(host, out var hostRecord)
            && hostRecord.LastStatAt is { } seenAt
            && IsAway(hostRecord.LastActivity)
            && _time.GetUtcNow() - seenAt < _settings.HostAfkCheckInterval)
        {
            SkipHost();
        }
    }

    private void AfkOnPlayerLeft(Player player)
    {
        _afk.Remove(player);
        if (Room.Players.Count == 0)
        {
            StopHostAfkCheck();
        }
    }

    private void AfkOnMatchStarted()
    {
        _mapChangedSinceMatch = false;
        StopHostAfkCheck();
        foreach (var player in Room.Players)
        {
            if (_afk.TryGetValue(player, out var record))
            {
                record.Playing = true;
                record.Score = -1;
            }
        }
    }

    private void AfkOnPlayerFinished(Player player, long score)
    {
        if (_afk.TryGetValue(player, out var record))
        {
            record.Score = score;
        }
    }

    private void AfkOnMatchFinished()
    {
        foreach (var (player, record) in _afk.ToList())
        {
            if (record.Playing)
            {
                record.Playing = false;
                ChangeAfkPoints(player, record.Score switch
                {
                    -1 => PointsNoMap,
                    0 => PointsNoScore,
                    _ => PointsScored,
                });
            }
        }
    }

    private void AfkOnMatchAborted()
    {
        foreach (var record in _afk.Values)
        {
            record.Playing = false;
        }
    }

    private void AfkOnChat(Player player)
    {
        ChangeAfkPoints(player, PointsChat);
        if (player == Room.Host)
        {
            StartHostAfkCheck(first: false);
        }
    }

    private void AfkOnStat(PlayerStat stat, bool viaPrivateMessage)
    {
        if (Room.Find(stat.Username) is not { } player || !Room.Contains(player))
        {
            return;
        }

        if (_afk.TryGetValue(player, out var record))
        {
            record.LastActivity = stat.Activity;
            record.LastStatAt = _time.GetUtcNow();
        }

        if (stat.Activity == PlayerActivity.Afk)
        {
            ChangeAfkPoints(player, PointsAwayStat);
        }

        // The quiet check came back: if the host looks away, ask again where players can see it.
        if (viaPrivateMessage && player == _hostAfkTarget)
        {
            _hostAfkTarget = null;
            Timers.Cancel(HostAfkReplyTimer);
            if (player == Room.Host && IsAway(stat.Activity))
            {
                _output.Add(new RequestStat(player.Name, ViaPrivateMessage: false));
            }

            StartHostAfkCheck(first: false);
            return;
        }

        if (!viaPrivateMessage && player == Room.Host && !Room.IsMatching && IsAway(stat.Activity))
        {
            if (_settings.SkipAfkHost)
            {
                SkipHost();
            }
            else
            {
                Announce(_mapChangedSinceMatch
                    ? "Players can start the match with !start to vote."
                    : "Players can skip the AFK host with !skip to vote.");
            }
        }
    }

    private void StartHostAfkCheck(bool first)
    {
        StopHostAfkCheck();
        if (_settings.HostAfkCheckInterval <= TimeSpan.Zero || Room.Host is not { } host || Room.IsMatching)
        {
            return;
        }

        var delay = first ? _settings.HostAfkFirstCheck : _settings.HostAfkCheckInterval;
        Timers.Start(HostAfkTimer, delay, () => CheckHostAfk(host));
    }

    private void CheckHostAfk(Player target)
    {
        // The timer stops when a match starts, but a cleared host leaves it running.
        if (Room.Host != target)
        {
            return;
        }

        _hostAfkTarget = target;
        _output.Add(new RequestStat(target.Name, ViaPrivateMessage: true));
        Timers.Start(HostAfkReplyTimer, _settings.StatReplyTimeout, () => StartHostAfkCheck(first: false));
    }

    private void StopHostAfkCheck()
    {
        _hostAfkTarget = null;
        Timers.Cancel(HostAfkTimer);
        Timers.Cancel(HostAfkReplyTimer);
    }

    private void ChangeAfkPoints(Player player, int delta)
    {
        if (!_settings.AfkKickEnabled || IsOwner(player.Name) || !_afk.TryGetValue(player, out var record))
        {
            return;
        }

        var now = _time.GetUtcNow();
        if (now < record.LastChange + _settings.AfkKickCooldown)
        {
            return;
        }

        record.LastChange = now;
        record.Points = Math.Max(0, record.Points + delta);
        if (record.Points >= _settings.AfkKickThreshold)
        {
            // Reset so the kick is not repeated before Bancho reports the player gone.
            record.Points = 0;
            _output.Add(new KickPlayer(player.Name));
            Announce("Kicked an AFK player.");
        }
    }

    private sealed class AfkRecord(DateTimeOffset joinedAt)
    {
        public int Points { get; set; }
        public DateTimeOffset LastChange { get; set; } = joinedAt;
        public bool Playing { get; set; }
        public long Score { get; set; } = -1;
        public PlayerActivity LastActivity { get; set; }
        public DateTimeOffset? LastStatAt { get; set; }
    }
}
