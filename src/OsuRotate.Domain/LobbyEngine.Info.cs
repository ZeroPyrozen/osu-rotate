namespace OsuRotate.Domain;

// Commands that only answer: !info and !help (also by private message), !version, !update from
// osu-ahr's MapRecaster, and !mirror and !rank from its MiscLoader. Each answer has a cooldown so a
// player cannot flood the lobby with it.
public sealed partial class LobbyEngine
{
    public const string VersionPlaceholder = "${version}";
    public static readonly TimeSpan MinimumInfoAnnouncementInterval = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan MirrorCooldown = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RankCooldown = TimeSpan.FromSeconds(10);
    private const string InfoAnnouncementTimer = "info-announcement";

    private bool _canRecast = true;
    private BeatmapInfo? _currentMap;
    private int _mirrorPendingFor;

    private string InfoMessage => _settings.InfoMessage.Replace(VersionPlaceholder, _settings.BotVersion, StringComparison.Ordinal);

    private void ShowInfo()
    {
        if (CooledDown("info", _settings.InfoMessageCooldown))
        {
            Announce(InfoMessage);
        }
    }

    private void ShowVersion()
    {
        if (CooledDown("version", _settings.InfoMessageCooldown))
        {
            Announce($"osu-rotate v. {_settings.BotVersion}");
        }
    }

    // osu-ahr answered !info and !help in private only to players in the lobby.
    private void OnPrivateMessage(string username, string text)
    {
        var message = text.Trim();
        if ((message.Equals("!info", StringComparison.OrdinalIgnoreCase) || message.Equals("!help", StringComparison.OrdinalIgnoreCase))
            && Room.Find(username) is { } player && Room.Contains(player)
            && CooledDown($"info-pm:{player.Key}", _settings.InfoMessageCooldown))
        {
            _output.Add(new Whisper(player.Name, InfoMessage));
        }
    }

    // Only intervals over 3 minutes announce, as in osu-ahr.
    private void StartInfoAnnouncement()
    {
        if (_settings.InfoAnnouncementInterval > MinimumInfoAnnouncementInterval)
        {
            Timers.Start(InfoAnnouncementTimer, _settings.InfoAnnouncementInterval, OnInfoAnnouncement);
        }
        else
        {
            Timers.Cancel(InfoAnnouncementTimer);
        }
    }

    private void OnInfoAnnouncement()
    {
        ShowInfo();
        StartInfoAnnouncement();
    }

    private void InfoOnBeatmapChanged(BeatmapChanged e)
    {
        // The bot's own !mp map (a recast or a regulation revert) does not allow another !update.
        if (!e.ChangedByReferee)
        {
            _canRecast = true;
        }

        if (_mirrorPendingFor != e.BeatmapId)
        {
            _mirrorPendingFor = 0;
        }
    }

    // Puts the current map up again, which makes clients with an old version of it update.
    private void Recast()
    {
        if (_canRecast && Room.BeatmapId != 0)
        {
            _canRecast = false;
            _output.Add(new SetBeatmap(Room.BeatmapId));
        }
    }

    private void ShowMirror()
    {
        var beatmapId = Room.BeatmapId;
        if (beatmapId == 0 || !CooledDown("mirror", MirrorCooldown))
        {
            return;
        }

        if (_currentMap is { } map && map.Id == beatmapId)
        {
            AnnounceMirror(map);
            return;
        }

        if (_mirrorPendingFor == beatmapId)
        {
            return;
        }

        // The map checker may already be looking this map up; its answer serves the mirror too.
        _mirrorPendingFor = beatmapId;
        if (_checkingMapId != beatmapId)
        {
            _output.Add(new LookUpBeatmap(beatmapId, Regulation.Mode, Regulation.AllowConvert));
        }
    }

    // A cached map is only used while its id is the room's, so an answer for an older map does no harm.
    private void InfoOnLookedUp(int beatmapId, BeatmapLookup lookup)
    {
        var found = lookup is { Outcome: BeatmapLookupOutcome.Found, Beatmap: { } map } ? map : null;
        if (found is not null)
        {
            _currentMap = found;
        }

        // A map change clears the request, so an answer for an older map never gets here.
        if (_mirrorPendingFor != beatmapId)
        {
            return;
        }

        _mirrorPendingFor = 0;
        if (found is not null)
        {
            AnnounceMirror(found);
        }
        else if (lookup.Outcome != BeatmapLookupOutcome.Failed)
        {
            Announce("The current beatmap doesn't have a mirror.");
        }
    }

    private void AnnounceMirror(BeatmapInfo map) =>
        Announce($"Alternative download link for beatmap {map.Title}: "
            + $"[https://beatconnect.io/b/{map.BeatmapsetId} BeatConnect.io] | [https://osu.direct/d/{map.BeatmapsetId} osu.direct]");

    // BanchoBot's !stats answer shows the rank to everyone. osu-ahr asked the osu! API when it could;
    // that can come with the API client in phase 4.
    private void ShowRank(string username)
    {
        if (CooledDown("rank", RankCooldown))
        {
            _output.Add(new RequestStat(username, ViaPrivateMessage: false));
        }
    }
}
