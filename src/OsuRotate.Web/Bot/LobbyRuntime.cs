using OsuRotate.Application;
using OsuRotate.Domain;

namespace OsuRotate.Web.Bot;

// Where the dashboard reaches the running lobby. The actor appears once the bot host has recovered
// the saved lobby; until then, and with no actor at all, the lobby reads as Idle.
public sealed class LobbyRuntime : ILobbyStatusReader
{
    private LobbyActor? _actor;

    // Raised with each new snapshot, and with null when the bot stops. Raised on the actor's loop:
    // handlers must only hand the snapshot on (Blazor pages use InvokeAsync), never wait.
    public event Action<LobbySnapshot?>? Changed;

    public LobbyActor? Actor => Volatile.Read(ref _actor);

    public LobbySnapshot? Snapshot => Actor?.Snapshot;

    public LobbyStatus Current => Snapshot?.Status ?? LobbyStatus.Idle;

    // False when the bot is not running yet; the dashboard says so instead of losing the action.
    public async ValueTask<bool> PostAsync(LobbyInput input, CancellationToken cancellationToken)
    {
        if (Actor is not { } actor)
        {
            return false;
        }

        await actor.PostAsync(input, cancellationToken).ConfigureAwait(false);
        return true;
    }

    internal void Attach(LobbyActor? actor)
    {
        if (Actor is { } previous)
        {
            previous.SnapshotChanged -= Raise;
        }

        Volatile.Write(ref _actor, actor);
        if (actor is not null)
        {
            actor.SnapshotChanged += Raise;
        }

        Raise(actor?.Snapshot);
    }

    // For tests: pages must stop listening when they close.
    internal int ListenerCount => Changed?.GetInvocationList().Length ?? 0;

    private void Raise(LobbySnapshot? snapshot) => Changed?.Invoke(snapshot);
}
