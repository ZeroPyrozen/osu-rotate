using OsuRotate.Domain;

namespace OsuRotate.Application;

public sealed record PlayerSnapshot(string Name, int? OsuUserId, int Slot, Team Team, MatchStatus Status);

// What the dashboard sees. Immutable, so a slow page cannot hold up the actor.
public sealed record LobbySnapshot(
    IReadOnlyList<PlayerSnapshot> Players,
    string? Host,
    IReadOnlyList<string> Queue,
    bool IsMatching,
    int BeatmapId,
    IReadOnlyList<string> Denied,
    IReadOnlyList<string> Referees,
    LobbySettings Settings)
{
    // Compared by content, so the actor only announces snapshots that changed.
    public bool Equals(LobbySnapshot? other) =>
        other is not null
        && Players.SequenceEqual(other.Players)
        && Host == other.Host
        && Queue.SequenceEqual(other.Queue)
        && IsMatching == other.IsMatching
        && BeatmapId == other.BeatmapId
        && Denied.SequenceEqual(other.Denied)
        && Referees.SequenceEqual(other.Referees)
        && Settings == other.Settings;

    public override int GetHashCode() => HashCode.Combine(Host, Queue.Count, Players.Count, IsMatching, BeatmapId, Settings);

    public static LobbySnapshot Of(LobbyEngine engine)
    {
        var room = engine.Room;
        return new LobbySnapshot(
            [.. room.Players.OrderBy(p => p.Slot).Select(p => new PlayerSnapshot(p.Name, p.OsuUserId, p.Slot, p.Team, p.Status))],
            room.Host?.Name,
            [.. engine.Rotation.Queue.Select(p => p.Name)],
            room.IsMatching,
            room.BeatmapId,
            [.. engine.DenyList.Keys.Order(StringComparer.Ordinal)],
            [.. room.Referees.Order(StringComparer.Ordinal)],
            engine.Settings);
    }
}
