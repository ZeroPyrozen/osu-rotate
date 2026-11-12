using OsuRotate.Domain;

namespace OsuRotate.Application;

// What survives a restart (design figure 11): the owner's rules, the deny list, and the open room with
// its queue and host. Votes and AFK points are left out on purpose; they reset on restart.
public interface ILobbyStore
{
    ValueTask<SavedLobby> LoadAsync(CancellationToken cancellationToken);

    // Replaces what was saved. A different room, or none, closes the saved session.
    ValueTask SaveAsync(SavedLobby lobby, CancellationToken cancellationToken);
}

// Settings is null until the owner's rules have been saved once.
public sealed record SavedLobby(SavedSession? Session, IReadOnlyList<string> Denied, LobbySettings? Settings)
{
    public static SavedLobby Empty { get; } = new(null, [], null);

    public bool Equals(SavedLobby? other) =>
        other is not null && Session == other.Session && Denied.SequenceEqual(other.Denied) && Settings == other.Settings;

    public override int GetHashCode() => HashCode.Combine(Session, Denied.Count, Settings);

    public static SavedLobby Of(LobbyEngine engine, LobbyLifecycle lifecycle) => new(
        lifecycle.RoomId is { } roomId
            ? new SavedSession(roomId, lifecycle.Title ?? LobbyLifecycle.DefaultTitle, [.. engine.Rotation.Queue.Select(p => p.Name)], engine.Room.Host?.Name)
            : null,
        [.. engine.DenyList.Keys.Order(StringComparer.Ordinal)],
        engine.Settings);
}

public sealed record SavedSession(long RoomId, string Title, IReadOnlyList<string> Queue, string? Host)
{
    public bool Equals(SavedSession? other) =>
        other is not null && RoomId == other.RoomId && Title == other.Title && Queue.SequenceEqual(other.Queue) && Host == other.Host;

    public override int GetHashCode() => HashCode.Combine(RoomId, Title, Queue.Count, Host);
}

// For tests and for running without a database.
public sealed class InMemoryLobbyStore : ILobbyStore
{
    private SavedLobby _saved = SavedLobby.Empty;

    public SavedLobby Saved => Volatile.Read(ref _saved);

    public ValueTask<SavedLobby> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Saved);

    public ValueTask SaveAsync(SavedLobby lobby, CancellationToken cancellationToken)
    {
        Volatile.Write(ref _saved, lobby);
        return ValueTask.CompletedTask;
    }
}
