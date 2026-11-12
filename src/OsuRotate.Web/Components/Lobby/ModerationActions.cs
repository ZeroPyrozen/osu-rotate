using OsuRotate.Application;
using OsuRotate.Domain;
using OsuRotate.Web.Bot;

namespace OsuRotate.Web.Components.Lobby;

// UC7: chat as the owner, kick, and the deny list. Each action checks the lobby again and returns null
// or a message to show.
public sealed class ModerationActions(LobbyRuntime runtime)
{
    // Bancho splits long lines; this only stops a paste of a whole document.
    public const int MaxChatLength = 400;

    private static bool IsOpen(LobbySnapshot? snapshot) => snapshot is { Status: LobbyStatus.Open };

    public static bool CanChat(LobbySnapshot? snapshot) => IsOpen(snapshot);

    public static bool CanKick(LobbySnapshot? snapshot, string name) =>
        IsOpen(snapshot) && snapshot!.Players.Any(p => Player.KeyOf(p.Name) == Player.KeyOf(name));

    public Task<string?> SayAsync(string? text, CancellationToken cancellationToken = default)
    {
        var message = text?.Trim() ?? string.Empty;
        if (message.Length == 0)
        {
            return Task.FromResult<string?>("Type something to send.");
        }

        if (message.Length > MaxChatLength)
        {
            return Task.FromResult<string?>($"A message can be at most {MaxChatLength} characters.");
        }

        return PostAsync(new SayInLobby(message), CanChat, cancellationToken);
    }

    public Task<string?> KickAsync(string name, CancellationToken cancellationToken = default) =>
        PostAsync(new KickFromLobby(name), s => CanKick(s, name), cancellationToken);

    // Works whether or not they are in the room: a denied player is never queued.
    public Task<string?> DenyAsync(string? name, CancellationToken cancellationToken = default)
    {
        var player = name?.Trim() ?? string.Empty;
        return player.Length == 0
            ? Task.FromResult<string?>("Enter the player's osu! username.")
            : PostAsync(new DenyPlayer(player), _ => true, cancellationToken);
    }

    public Task<string?> AllowAsync(string name, CancellationToken cancellationToken = default) =>
        PostAsync(new AllowPlayer(name), s => s is not null && s.Denied.Contains(Player.KeyOf(name)), cancellationToken);

    private async Task<string?> PostAsync(LobbyInput input, Func<LobbySnapshot?, bool> allowed, CancellationToken cancellationToken)
    {
        if (runtime.Actor is null)
        {
            return LifecycleActions.NotRunning;
        }

        if (!allowed(runtime.Snapshot))
        {
            return "That is not possible right now; the lobby has changed. Look at its status above.";
        }

        return await runtime.PostAsync(input, cancellationToken).ConfigureAwait(false) ? null : LifecycleActions.NotRunning;
    }
}
