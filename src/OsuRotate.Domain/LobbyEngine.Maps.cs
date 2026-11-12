namespace OsuRotate.Domain;

// Map regulation, ported from osu-ahr's MapChecker. When the host picks a map the engine asks the
// application to look it up (LookUpBeatmap); the answer comes back as BeatmapLookedUp. A map outside
// the regulation is reverted to the last accepted map, and a host who keeps picking such maps loses
// the turn. An accepted map is set again with its mode and a description, and starts auto start.
public sealed partial class LobbyEngine
{
    private static readonly TimeSpan RegulationMessageCooldown = TimeSpan.FromSeconds(10);
    private const string StarRatingWarning = "Attention! Star rating will not be calculated correctly if a global mod is applied.";

    private int _lastAcceptedMapId;
    private int _checkingMapId;
    private string _checkingMapTitle = string.Empty;

    public int MapViolations { get; private set; }
    public int LastAcceptedMapId => _lastAcceptedMapId;

    private MapRegulation Regulation => _settings.Regulation;

    private void MapsOnBeatmapChanging() => _checkingMapId = 0;

    private void MapsOnBeatmapChanged(int beatmapId, string title)
    {
        if (!Regulation.Enabled)
        {
            return;
        }

        _checkingMapId = beatmapId;
        _checkingMapTitle = title;

        // The bot's own revert or re-set of an accepted map comes back here too.
        if (beatmapId == _lastAcceptedMapId)
        {
            return;
        }

        _output.Add(new LookUpBeatmap(beatmapId, Regulation.Mode, Regulation.AllowConvert));
    }

    private void MapsOnLookedUp(int beatmapId, BeatmapLookup lookup)
    {
        // The host has picked another map since.
        if (!Regulation.Enabled || beatmapId != _checkingMapId)
        {
            return;
        }

        var link = $"[https://osu.ppy.sh/b/{beatmapId} {_checkingMapTitle}]";
        var mode = Regulation.Mode.OfficialName();
        switch (lookup)
        {
            case { Outcome: BeatmapLookupOutcome.Found, Beatmap: { } map }:
                var (rate, message) = Regulation.Rate(map);
                if (rate > 0)
                {
                    RejectMap(message, showRegulation: true);
                }
                else
                {
                    AcceptMap(map);
                }

                break;
            case { Outcome: BeatmapLookupOutcome.NotFound }:
                RejectMap($"{link} had already been removed from the website.", showRegulation: false);
                break;
            case { Outcome: BeatmapLookupOutcome.ModeMismatch }:
                RejectMap($"{link} is not {mode} beatmap. Pick {mode} beatmap.", showRegulation: false);
                break;
            case { Outcome: BeatmapLookupOutcome.NotAvailable }:
                RejectMap($"{link} is not available for download.", showRegulation: false);
                break;
        }
    }

    private void MapsOnHostChanged()
    {
        _checkingMapId = 0;
        MapViolations = 0;
    }

    // A match started before the check finished: the map counts as accepted.
    private void MapsOnMatchStarted()
    {
        if (_checkingMapId != 0)
        {
            _lastAcceptedMapId = _checkingMapId;
        }

        _checkingMapId = 0;
        MapViolations = 0;
    }

    private void RejectMap(string reason, bool showRegulation)
    {
        MapViolations++;
        _checkingMapId = 0;

        // With no accepted map yet there is nothing to go back to, so only explain. osu-ahr sent "!mp map 0".
        var revert = _lastAcceptedMapId != 0;
        if (showRegulation)
        {
            if (revert)
            {
                _output.Add(new SetBeatmap(_lastAcceptedMapId, (int)Regulation.Mode, $"Current regulation: {Regulation.Describe()}"));
            }
            else
            {
                Announce($"Current regulation: {Regulation.Describe()}");
            }

            Announce(reason);
            Announce(StarRatingWarning);
        }
        else if (revert)
        {
            _output.Add(new SetBeatmap(_lastAcceptedMapId, (int)Regulation.Mode, reason));
        }
        else
        {
            Announce(reason);
        }

        if (Regulation.ViolationsAllowed > 0 && MapViolations >= Regulation.ViolationsAllowed)
        {
            Announce($"The number of violations has reached {Regulation.ViolationsAllowed}. Skipped player {Room.Host?.Name}");
            SkipHost();
        }
    }

    private void AcceptMap(BeatmapInfo map)
    {
        _lastAcceptedMapId = map.Id;
        _output.Add(new SetBeatmap(map.Id, (int)Regulation.Mode, Regulation.DescribeMap(map)));
        AutoStartOnMapAccepted();
    }

    private void ShowRegulation()
    {
        if (CooledDown("regulation", RegulationMessageCooldown))
        {
            Announce(Regulation.Describe());
        }
    }

    private void ChangeRegulation(string command, string argument)
    {
        var before = Regulation;
        if (RegulationCommand.Apply(before, command, argument) is not { } after || after == before)
        {
            return;
        }

        UpdateSettings(_settings with { Regulation = after });
        AnnounceRegulationChange(before, after);
    }
}
