using System.Globalization;
using OsuRotate.Application;
using OsuRotate.Domain;
using OsuRotate.Web.Bot;

namespace OsuRotate.Web.Components.Lobby;

// UC5 and UC6: reorder the queue, skip or hand over host, start, abort. These force the action
// regardless of votes. Each action checks the lobby again and returns null or a message.
public sealed class QueueActions(LobbyRuntime runtime)
{
    private static bool IsOpen(LobbySnapshot? snapshot) => snapshot is { Status: LobbyStatus.Open };

    public static bool CanReorder(LobbySnapshot? snapshot) => IsOpen(snapshot) && snapshot!.Queue.Count > 1;

    public static bool CanSkip(LobbySnapshot? snapshot) => IsOpen(snapshot) && snapshot!.Queue.Count > 1;

    public static bool CanStart(LobbySnapshot? snapshot) => IsOpen(snapshot) && !snapshot!.IsMatching && snapshot.Players.Count > 0;

    public static bool CanAbort(LobbySnapshot? snapshot) => IsOpen(snapshot) && snapshot!.IsMatching;

    public static bool CanCancelCountdown(LobbySnapshot? snapshot) => IsOpen(snapshot) && !snapshot!.IsMatching && snapshot.CountdownRunning;

    // The queue with one player moved one place up (-1) or down (+1); null if that is not a move.
    public static IReadOnlyList<string>? Moved(IReadOnlyList<string> queue, string name, int direction)
    {
        var index = queue.ToList().IndexOf(name);
        var target = index + direction;
        if (index < 0 || direction is not (-1 or 1) || target < 0 || target >= queue.Count)
        {
            return null;
        }

        var order = queue.ToList();
        (order[index], order[target]) = (order[target], order[index]);
        return order;
    }

    public static bool TryParseCountdown(string? text, out int seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        return int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out seconds) && seconds <= LobbyEngine.MaxCountdownSeconds;
    }

    public Task<string?> MoveAsync(string name, int direction, CancellationToken cancellationToken = default)
    {
        var order = runtime.Snapshot is { } snapshot ? Moved(snapshot.Queue, name, direction) : null;
        if (runtime.Actor is not null && order is null)
        {
            return Task.FromResult<string?>("That player cannot move there.");
        }

        // Names cannot contain commas, so a comma list is what *reorder takes.
        return PostAsync(new ReorderQueue(string.Join(", ", order ?? [])), CanReorder, cancellationToken);
    }

    public Task<string?> SkipHostAsync(CancellationToken cancellationToken = default) =>
        PostAsync(new SkipHost(), CanSkip, cancellationToken);

    public Task<string?> GiveHostAsync(string name, CancellationToken cancellationToken = default) =>
        PostAsync(new SkipToPlayer(name), s => CanSkip(s) && s!.Queue.Contains(name), cancellationToken);

    public Task<string?> StartAsync(string? countdown, CancellationToken cancellationToken = default) =>
        TryParseCountdown(countdown, out var seconds)
            ? PostAsync(new ForceStart(seconds), CanStart, cancellationToken)
            : Task.FromResult<string?>($"Enter a countdown from 0 to {LobbyEngine.MaxCountdownSeconds} seconds, or leave it empty to start now.");

    public Task<string?> AbortAsync(CancellationToken cancellationToken = default) =>
        PostAsync(new ForceAbort(), CanAbort, cancellationToken);

    public Task<string?> CancelCountdownAsync(CancellationToken cancellationToken = default) =>
        PostAsync(new CancelCountdown(), CanCancelCountdown, cancellationToken);

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
