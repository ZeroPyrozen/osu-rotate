namespace OsuRotate.Domain;

// !pick (and !suggest), map pick issue 04. The engine asks the application for a map (PickBeatmap);
// the answer comes back as BeatmapPicked. The host's pick is put in the room as an accepted map, and
// anyone else's is posted as a suggestion. Off unless the owner turns it on in the dashboard.
public sealed partial class LobbyEngine
{
    public static readonly TimeSpan PickPlayerCooldown = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan PickRoomCooldown = TimeSpan.FromSeconds(10);
    private const string PickRoomKey = "pick";
    private const string PickHelp = "- !pick finds a map that fits the rules. The host's !pick puts it in the room; anyone else's is a suggestion.";

    // A pick inside either cooldown is ignored without a reply, and doesn't count against the other.
    private void OnPickCommand(string requester)
    {
        var playerKey = $"pick:{Player.KeyOf(requester)}";
        if (!_settings.MapPick.Enabled || Room.IsMatching
            || CoolingDown(PickRoomKey, PickRoomCooldown) || CoolingDown(playerKey, PickPlayerCooldown))
        {
            return;
        }

        CooledDown(PickRoomKey, PickRoomCooldown);
        CooledDown(playerKey, PickPlayerCooldown);
        _output.Add(new PickBeatmap(requester, IsHost(requester), MapPickQuery.For(Regulation, _settings.MapPick)));
    }

    private void PickOnAnswered(BeatmapPicked answer)
    {
        var request = answer.Request;

        // Things may have changed while the pool answered. A host's pick is not turned into a suggestion.
        if (!_settings.MapPick.Enabled || Room.IsMatching || (request.AsHost && !IsHost(request.Requester)))
        {
            return;
        }

        switch (answer.Result)
        {
            case { Outcome: MapPickOutcome.Found, Beatmap: { } map }:
                // Checked against the rules as they are now, which may have changed meanwhile.
                if (!MapPickQuery.For(Regulation, _settings.MapPick).Fits(map))
                {
                    return;
                }

                if (request.AsHost)
                {
                    SetPickedMap(map);
                }
                else
                {
                    Announce($"Suggestion from {request.Requester}: {Regulation.DescribeMap(map)}");
                }

                break;
            case { Outcome: MapPickOutcome.NoneFit }:
                Announce("No maps fit the current rules.");
                break;
            case { Outcome: MapPickOutcome.Failed }:
                Announce("Couldn't reach osu! right now, try again later.");
                break;
        }
    }

    // Recorded as accepted before it is set, so the change coming back is not looked up and rated again.
    private void SetPickedMap(BeatmapInfo map)
    {
        _lastAcceptedMapId = map.Id;
        _rejectedMapId = 0;
        _currentMap = map;
        _output.Add(new SetBeatmap(map.Id, (int)Regulation.Mode, Regulation.DescribeMap(map)));

        // With the regulation off, the map change itself starts the auto start wait.
        if (Regulation.Enabled)
        {
            AutoStartOnMapAccepted();
        }
    }

    private bool IsHost(string name) => Room.Host is { } host && host.Key == Player.KeyOf(name);

    private bool CoolingDown(string key, TimeSpan cooldown) =>
        _lastSent.TryGetValue(key, out var last) && _time.GetUtcNow() - last < cooldown;
}
