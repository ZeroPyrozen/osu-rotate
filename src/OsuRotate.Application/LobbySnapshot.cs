using OsuRotate.Domain;

namespace OsuRotate.Application;

public sealed record PlayerSnapshot(string Name, int? OsuUserId, int Slot, Team Team, MatchStatus Status);

// What the dashboard sees. Immutable, so a slow page cannot hold up the actor.
public sealed record LobbySnapshot(
    LobbyStatus Status,
    bool Connected,
    long? RoomId,
    string? Title,
    bool ClosingWhenEmpty,
    IReadOnlyList<PlayerSnapshot> Players,
    string? Host,
    IReadOnlyList<string> Queue,
    bool IsMatching,
    bool CountdownRunning,
    int BeatmapId,
    IReadOnlyList<string> Denied,
    IReadOnlyList<string> Referees,
    LobbySettings Settings,
    string? Notice = null)
{
    // Compared by content, so the actor only announces snapshots that changed.
    public bool Equals(LobbySnapshot? other) =>
        other is not null
        && Status == other.Status
        && Connected == other.Connected
        && RoomId == other.RoomId
        && Title == other.Title
        && ClosingWhenEmpty == other.ClosingWhenEmpty
        && Players.SequenceEqual(other.Players)
        && Host == other.Host
        && Queue.SequenceEqual(other.Queue)
        && IsMatching == other.IsMatching
        && CountdownRunning == other.CountdownRunning
        && BeatmapId == other.BeatmapId
        && Denied.SequenceEqual(other.Denied)
        && Referees.SequenceEqual(other.Referees)
        && Settings == other.Settings
        && Notice == other.Notice;

    public override int GetHashCode() => HashCode.Combine(Status, RoomId, Host, Queue.Count, Players.Count, IsMatching, BeatmapId);

    public static LobbySnapshot Of(LobbyEngine engine, LobbyLifecycle lifecycle)
    {
        var room = engine.Room;
        return new LobbySnapshot(
            lifecycle.Status,
            lifecycle.Connected,
            lifecycle.RoomId,
            lifecycle.Title,
            lifecycle.ClosingWhenEmpty,
            [.. room.Players.OrderBy(p => p.Slot).Select(p => new PlayerSnapshot(p.Name, p.OsuUserId, p.Slot, p.Team, p.Status))],
            room.Host?.Name,
            [.. engine.Rotation.Queue.Select(p => p.Name)],
            room.IsMatching,
            engine.IsStartTimerRunning,
            room.BeatmapId,
            [.. engine.DenyList.Keys.Order(StringComparer.Ordinal)],
            [.. room.Referees.Order(StringComparer.Ordinal)],
            engine.Settings,
            lifecycle.Notice);
    }
}
