using System.Globalization;
using System.Text.RegularExpressions;
using OsuRotate.Domain;

namespace OsuRotate.Application;

// One line for each thing the lobby reacts to and each command it sends, for the journal, so a live
// session can be checked afterwards against what the engine should have done.
//
// Chat is never written: a player's chat line becomes "chat from <name>", plus its !command word
// if it starts with one, and the owner's own chat is only counted. The bot's announcements are its
// own words and are written; the room password never is. Returns null for what isn't worth a line
// (countdown ticks).
public static partial class LobbyTrace
{
    private const int MaxTextLength = 300;

    public static string? Describe(LobbyInput input) => input switch
    {
        null => throw new ArgumentNullException(nameof(input)),
        BanchoEvent e => Describe(e.Event),
        SkipHost => "owner: skip host",
        SkipToPlayer e => $"owner: host to {e.Name}",
        ReorderQueue e => $"owner: reorder queue to {e.Names}",
        DenyPlayer e => $"owner: deny {e.Name}",
        AllowPlayer e => $"owner: allow {e.Name}",
        ChangeSettings => "owner: rules changed",
        ForceStart e => Invariant($"owner: start match in {e.CountdownSeconds} s"),
        ForceAbort => "owner: abort match",
        CancelCountdown => "owner: cancel countdown",
        CreateLobby e => $"owner: create room \"{e.Title}\"",
        AttachLobby e => Invariant($"owner: attach to room {e.RoomId}"),
        CloseLobby => "owner: close room",
        CloseLobbyWhenEmpty e => e.Enabled ? "owner: close room when empty" : "owner: keep room open when empty",
        KickFromLobby e => $"owner: kick {e.Name}",
        SayInLobby e => Invariant($"owner: chat ({e.Text.Length} characters)"),
        _ => $"owner: {input.GetType().Name}",
    };

    public static string? Describe(LobbyEvent lobbyEvent) => lobbyEvent switch
    {
        null => throw new ArgumentNullException(nameof(lobbyEvent)),
        CountdownTick => null,
        TransportConnected => "connected to Bancho",
        TransportDisconnected e => $"disconnected from Bancho: {e.Reason}",
        TransportAuthenticationFailed => "Bancho refused the login",
        RoomCreated e => Invariant($"room {e.RoomId} created: \"{e.Title}\""),
        RoomJoined e => Invariant($"joined room {e.RoomId}"),
        RoomJoinFailed e => Invariant($"could not join room {e.RoomId}: {e.Reason}"),
        RoomClosed e => Invariant($"room {e.RoomId} closed"),
        RoomStateReceived e => Invariant(
            $"room state: {e.State.Players.Count} players, host {e.State.Players.FirstOrDefault(p => p.IsHost)?.Username ?? "none"}, map {e.State.BeatmapId}"),
        RoomSizeChanged e => Invariant($"room size {e.Size}"),
        PlayerJoined e => Invariant($"joined: {e.Username} (slot {e.Slot})"),
        PlayerLeft e => $"left: {e.Username}",
        PlayerMoved e => Invariant($"moved: {e.Username} to slot {e.Slot}"),
        PlayerKicked e => $"kicked: {e.Username}",
        PlayerStatReceived e => Invariant($"stat: {e.Stat.Username} is {e.Stat.Activity}"),
        RefereeAdded e => $"referee added: {e.Username}",
        RefereeRemoved e => $"referee removed: {e.Username}",
        HostChanged e => $"host is now {e.Username}",
        HostTransferAcknowledged e => $"host given to {e.Username}",
        HostCleared => "host cleared",
        BeatmapChanged e => Invariant($"map picked: {e.BeatmapId} \"{e.Title}\"{(e.ChangedByReferee ? " (by referee)" : string.Empty)}"),
        BeatmapLookedUp e => Describe(e),
        MatchHistoryRead e => Invariant($"match history read: {e.Kicks.Count} kicks, up to event {e.LatestEventId}"),
        CountdownStarted e => Invariant($"countdown {e.Seconds} s"),
        PlayerFinished e => Invariant($"finished: {e.Username} {e.Score}{(e.Passed ? string.Empty : " (failed)")}"),
        ChatReceived e => $"chat from {e.Username}{CommandWord(e.Text)}",
        PrivateMessageReceived e => $"private message from {e.Username}{CommandWord(e.Text)}",
        _ => Words(lobbyEvent.GetType().Name),
    };

    public static string Describe(LobbyCommand command) => command switch
    {
        null => throw new ArgumentNullException(nameof(command)),
        CreateRoom e => $"!mp make \"{e.Title}\"",
        JoinRoom e => Invariant($"join room {e.RoomId}"),
        CloseRoom => "!mp close",
        RequestRoomState => "!mp settings",
        RequestReferees => "!mp listrefs",
        RequestStat e => $"!stats {e.Username}",
        TransferHost e => $"!mp host {e.Username}",
        ClearHost => "!mp clearhost",
        StartMatch e => e.CountdownSeconds is { } seconds ? Invariant($"!mp start {seconds}") : "!mp start",
        AbortMatch => "!mp abort",
        AbortCountdown => "!mp aborttimer",
        KickPlayer e => $"!mp kick {e.Username}",
        InvitePlayer e => $"!mp invite {e.Username}",
        LookUpBeatmap e => Invariant($"look up map {e.BeatmapId} ({e.Mode}{(e.AllowConvert ? ", converts allowed" : string.Empty)})"),
        ReadMatchHistory e => e.AfterEventId is { } after ? Invariant($"read match history after event {after}") : "read match history",
        RecordMatch e => Invariant($"record match: map {e.Match.BeatmapId}, host {e.Match.HostName ?? "none"}, {e.Match.Outcome}, {e.Match.Scores.Count} scores"),
        SetBeatmap e => Invariant($"!mp map {e.BeatmapId}{(e.GameMode is { } mode ? $" {mode}" : string.Empty)}"),
        SetMods e => $"!mp mods {e.Mods}",
        SetRoomName e => $"!mp name \"{e.Name}\"",
        SetPassword e => string.IsNullOrEmpty(e.Password) ? "!mp password (removed)" : "!mp password (set, not shown)",
        SetSize e => Invariant($"!mp size {e.Size}"),
        SetTeamAndScoreMode e => Invariant($"!mp set {e.TeamMode} {e.ScoreMode}{(e.Size is { } size ? $" {size}" : string.Empty)}"),
        Say e => $"say: {OneLine(e.Text)}",
        Whisper e => $"whisper to {e.Username}: {OneLine(e.Text)}",
        _ => Words(command.GetType().Name),
    };

    private static string Describe(BeatmapLookedUp e) => e.Result.Beatmap is { } map
        ? Invariant($"map {e.BeatmapId} looked up: {e.Result.Outcome}, {map.Mode}, {map.StarRating:0.00}*, {map.LengthSeconds} s")
        : Invariant($"map {e.BeatmapId} looked up: {e.Result.Outcome}");

    // " (!skip)" for a chat line that starts with a command word, nothing for anything else.
    private static string CommandWord(string text)
    {
        var match = CommandPattern().Match(text);
        return match.Success ? $" ({match.Groups[1].Value.ToLowerInvariant()})" : string.Empty;
    }

    private static string OneLine(string text)
    {
        var line = string.Join(" / ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return line.Length <= MaxTextLength ? line : string.Concat(line.AsSpan(0, MaxTextLength), "...");
    }

    // "MatchFinished" -> "match finished".
    private static string Words(string typeName) => UpperCase().Replace(typeName, " $0").Trim().ToLowerInvariant();

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^\s*(![A-Za-z]{1,20})(?:\s|$)")]
    private static partial Regex CommandPattern();

    [GeneratedRegex("[A-Z]")]
    private static partial Regex UpperCase();
}
