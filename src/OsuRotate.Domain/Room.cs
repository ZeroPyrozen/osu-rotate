namespace OsuRotate.Domain;

// What the bot believes about the multiplayer room. When Bancho reports something that contradicts
// it, the caller asks for !mp settings and Merge brings it back in line.
public sealed class Room
{
    public const int MaxPlayers = 16;

    private readonly Dictionary<string, Player> _known = [];
    private readonly List<Player> _present = [];

    public IReadOnlyList<Player> Players => _present;
    public Player? Host { get; private set; }
    public Player? PendingHost { get; internal set; }
    public bool IsMatching { get; private set; }
    public bool IsHostCleared { get; private set; }
    public int BeatmapId { get; internal set; }

    public Player GetOrAdd(string name)
    {
        var key = Player.KeyOf(name);
        if (!_known.TryGetValue(key, out var player))
        {
            player = new Player(name);
            _known[key] = player;
        }

        return player;
    }

    public Player? Find(string name) => _known.GetValueOrDefault(Player.KeyOf(name));

    public bool Contains(Player player) => _present.Contains(player);

    public bool TryAdd(Player player, int slot, Team team)
    {
        player.Slot = slot;
        player.Team = team;
        player.Status = MatchStatus.InLobby;
        if (_present.Contains(player) || _present.Count >= MaxPlayers)
        {
            return false;
        }

        _present.Add(player);
        return true;
    }

    public bool TryRemove(Player player)
    {
        player.Status = MatchStatus.None;
        if (!_present.Remove(player))
        {
            return false;
        }

        if (Host == player)
        {
            Host = null;
        }

        if (PendingHost == player)
        {
            PendingHost = null;
        }

        return true;
    }

    public bool TrySetHost(Player player)
    {
        IsHostCleared = false;
        if (!_present.Contains(player))
        {
            return false;
        }

        if (PendingHost == player)
        {
            PendingHost = null;
        }

        Host = player;
        return true;
    }

    public void ClearHost()
    {
        IsHostCleared = true;
        Host = null;
        PendingHost = null;
    }

    public void StartMatch()
    {
        IsMatching = true;
        foreach (var player in _present)
        {
            player.Status = MatchStatus.Playing;
        }
    }

    // Returns how many players had finished, which decides whether an abort counts as a played map.
    public int EndMatch()
    {
        var finished = _present.Count(p => p.Status == MatchStatus.Finished);
        IsMatching = false;
        foreach (var player in _present)
        {
            player.Status = MatchStatus.InLobby;
        }

        return finished;
    }

    public RoomChanges Merge(RoomState state)
    {
        BeatmapId = state.BeatmapId;
        var reported = state.Players.Select(p => (Slot: p, Player: GetOrAdd(p.Username))).ToList();
        var left = _present.Where(p => reported.All(r => r.Player != p)).ToList();
        foreach (var player in left)
        {
            TryRemove(player);
        }

        var joined = new List<Player>();
        var hostChanged = false;
        foreach (var (slot, player) in reported)
        {
            player.OsuUserId = slot.OsuUserId;
            if (!_present.Contains(player))
            {
                TryAdd(player, slot.Slot, slot.Team);
                joined.Add(player);
            }
            else
            {
                player.Slot = slot.Slot;
                player.Team = slot.Team;
            }

            if (slot.IsHost && player != Host)
            {
                TrySetHost(player);
                hostChanged = true;
            }
        }

        return new RoomChanges(joined, left, hostChanged);
    }
}

public sealed record RoomChanges(IReadOnlyList<Player> Joined, IReadOnlyList<Player> Left, bool HostChanged)
{
    public bool Any => HostChanged || Joined.Count > 0 || Left.Count > 0;
}
