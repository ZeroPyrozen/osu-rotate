using OsuRotate.Application;
using OsuRotate.Domain;

namespace OsuRotate.Web.Bot;

// Where the dashboard reaches the running lobby. The actor appears once the bot host has recovered
// the saved lobby; until then, and with no actor at all, the lobby reads as Idle.
public sealed class LobbyRuntime : ILobbyStatusReader
{
    private LobbyActor? _actor;

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

    internal void Attach(LobbyActor? actor) => Volatile.Write(ref _actor, actor);
}
