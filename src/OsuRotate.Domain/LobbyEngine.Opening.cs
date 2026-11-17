namespace OsuRotate.Domain;

// The opening map (map pick, issue 09). A room fresh from !mp make has no map the bot chose, so the
// first map was whatever the first host had, and with nothing accepted yet a rejected map stayed in
// the room. When the bot makes a room it asks for a map that fits the rules (ChooseOpeningMap): the
// newest one from the match history, else one from the map pool while !pick is on. It goes in the room
// as an accepted map. If a host's own map is already being checked, it is only kept to revert to.
public sealed partial class LobbyEngine
{
    // Any map change seen in this room, the bot's own included.
    private bool _roomMapChanged;

    // The opening map until its change comes back. It waits for a host rather than starting auto start.
    private int _openingMapId;

    private void OpeningOnRoomCreated() =>
        _output.Add(new ChooseOpeningMap(MapPickQuery.For(Regulation, _settings.MapPick), Regulation.AllowConvert, _settings.MapPick.Enabled));

    private void OpeningOnBeatmapChanged() => _roomMapChanged = true;

    // True for the bot's own opening map coming back, once.
    private bool IsOpeningMapChange(int beatmapId)
    {
        if (_openingMapId == 0 || beatmapId != _openingMapId)
        {
            return false;
        }

        _openingMapId = 0;
        return true;
    }

    private void OpeningOnChosen(OpeningMapChosen answer)
    {
        // Checked against the rules as they are now, which may have changed meanwhile.
        if (answer.Beatmap is not { } map || Room.IsMatching || !MapPickQuery.For(Regulation, _settings.MapPick).Fits(map))
        {
            return;
        }

        if (!_roomMapChanged || CurrentMapRejected)
        {
            // Recorded as accepted before it is set, so the change coming back is not looked up again.
            _lastAcceptedMapId = map.Id;
            _rejectedMapId = 0;
            _currentMap = map;
            _openingMapId = map.Id;
            _output.Add(new SetBeatmap(map.Id, (int)Regulation.Mode, Regulation.DescribeMap(map)));
        }
        else if (Regulation.Enabled && _lastAcceptedMapId == 0)
        {
            // A host's map is being checked: leave it, but give a rejection something to go back to.
            _lastAcceptedMapId = map.Id;
        }
    }
}
