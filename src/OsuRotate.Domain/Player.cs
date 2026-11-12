namespace OsuRotate.Domain;

public enum MatchStatus
{
    None,
    InLobby,
    Playing,
    Finished,
}

// One instance per person for the lobby's lifetime, so players can be compared by reference.
public sealed class Player
{
    internal Player(string name)
    {
        Name = name;
        Key = KeyOf(name);
    }

    public string Name { get; }
    public string Key { get; }
    public int? OsuUserId { get; internal set; }
    public int Slot { get; internal set; }
    public Team Team { get; internal set; }
    public MatchStatus Status { get; internal set; }

    // BanchoBot writes "a b", IRC writes "a_b", and osu! names are case-insensitive.
    public static string KeyOf(string name) => PlayerNames.ToIrcName(name.Trim()).ToLowerInvariant();

    public override string ToString() => Name;
}
