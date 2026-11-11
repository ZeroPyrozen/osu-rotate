namespace OsuRotate.Domain;

// Players who may stay in the room but never get host. Keyed by name so it can be filled before
// the player joins.
public sealed class DenyList
{
    private readonly HashSet<string> _keys = [];

    public IReadOnlyCollection<string> Keys => _keys;

    public bool Contains(Player player) => _keys.Contains(player.Key);

    public bool Add(string name) => _keys.Add(Player.KeyOf(name));

    public bool Remove(string name) => _keys.Remove(Player.KeyOf(name));
}
