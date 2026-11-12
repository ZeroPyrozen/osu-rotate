using System.Globalization;
using System.Text.RegularExpressions;
using OsuRotate.Application;
using OsuRotate.Domain;
using OsuRotate.Web.Bot;

namespace OsuRotate.Web.Components.Lobby;

// UC1 to UC3: what the lifecycle buttons may do in each state, and posting them. Kept out of the
// component so it can be tested without a browser. Each action returns null, or a message to show.
public sealed partial class LifecycleActions(LobbyRuntime runtime)
{
    public const string NotRunning = "The bot is not running yet. Try again in a moment.";

    public static bool CanOpen(LobbySnapshot? snapshot) => snapshot is { RoomId: null, Status: LobbyStatus.Idle };

    // Also stops a create or attach that is still waiting for Bancho.
    public static bool CanClose(LobbySnapshot? snapshot) => snapshot is not null && snapshot.Status is not (LobbyStatus.Idle or LobbyStatus.Closing);

    public static bool CanCloseWhenEmpty(LobbySnapshot? snapshot) => snapshot is { Status: LobbyStatus.Open };

    // The kept title if there is one, so a new room starts as the keeper would make it anyway.
    public static string DefaultTitle(LobbySnapshot? snapshot) =>
        snapshot?.Settings.Keeping.Title is { Length: > 0 } kept ? kept : LobbyLifecycle.DefaultTitle;

    public async Task<string?> CreateAsync(string? title, CancellationToken cancellationToken = default)
    {
        var name = string.IsNullOrWhiteSpace(title) ? DefaultTitle(runtime.Snapshot) : title.Trim();
        if (name.Length > RoomKeeping.MaxTitleLength)
        {
            return $"A room title can be at most {RoomKeeping.MaxTitleLength} characters.";
        }

        return await PostAsync(new CreateLobby(name), CanOpen, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> AttachAsync(string? room, CancellationToken cancellationToken = default)
    {
        if (!TryParseRoom(room, out var roomId))
        {
            return "Enter a room number, or a link like https://osu.ppy.sh/mp/123456.";
        }

        return await PostAsync(new AttachLobby(roomId), CanOpen, cancellationToken).ConfigureAwait(false);
    }

    public Task<string?> CloseAsync(CancellationToken cancellationToken = default) =>
        PostAsync(new CloseLobby(), CanClose, cancellationToken);

    public Task<string?> CloseWhenEmptyAsync(bool enabled, CancellationToken cancellationToken = default) =>
        PostAsync(new CloseLobbyWhenEmpty(enabled), CanCloseWhenEmpty, cancellationToken);

    // A room number, #number, or an osu.ppy.sh/mp/ or /community/matches/ link.
    public static bool TryParseRoom(string? text, out long roomId)
    {
        roomId = 0;
        var match = RoomPattern().Match(text?.Trim() ?? string.Empty);
        return match.Success
            && long.TryParse(match.Groups["id"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out roomId)
            && roomId > 0;
    }

    // The lobby may have moved on since the page last rendered, so the state is checked again here.
    private async Task<string?> PostAsync(LobbyInput input, Func<LobbySnapshot?, bool> allowed, CancellationToken cancellationToken)
    {
        if (runtime.Actor is null)
        {
            return NotRunning;
        }

        if (!allowed(runtime.Snapshot))
        {
            return "That is not possible right now; the lobby has changed. Look at its status above.";
        }

        return await runtime.PostAsync(input, cancellationToken).ConfigureAwait(false) ? null : NotRunning;
    }

    [GeneratedRegex(@"^(?:(?:https?://)?osu\.ppy\.sh/(?:mp|community/matches)/|#)?(?<id>[0-9]{1,18})/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RoomPattern();
}
