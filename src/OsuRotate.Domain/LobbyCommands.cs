namespace OsuRotate.Domain;

// Lower values are sent first when the outbound rate limit is reached.
public enum CommandPriority
{
    Control,
    Admin,
    Info,
}

public abstract record LobbyCommand
{
    public virtual CommandPriority Priority => CommandPriority.Control;
}

public sealed record CreateRoom(string Title) : LobbyCommand;
public sealed record JoinRoom(long RoomId) : LobbyCommand;
public sealed record CloseRoom : LobbyCommand;
public sealed record RequestRoomState : LobbyCommand;
public sealed record RequestReferees : LobbyCommand;
public sealed record RequestStat(string Username, bool ViaPrivateMessage) : LobbyCommand;

public sealed record TransferHost(string Username, int? OsuUserId = null) : LobbyCommand;
public sealed record ClearHost : LobbyCommand;
public sealed record StartMatch(int? CountdownSeconds = null) : LobbyCommand;
public sealed record AbortMatch : LobbyCommand;
public sealed record AbortCountdown : LobbyCommand;
public sealed record KickPlayer(string Username) : LobbyCommand;
public sealed record InvitePlayer(string Username) : LobbyCommand;

// Text after a | in !mp map is shown to players, which is how map details reach the lobby.
public sealed record SetBeatmap(int BeatmapId, int? GameMode = null, string? Note = null) : LobbyCommand;
public sealed record SetMods(string Mods) : LobbyCommand;
public sealed record SetRoomName(string Name) : LobbyCommand;
public sealed record SetPassword(string? Password) : LobbyCommand;
public sealed record SetSize(int Size) : LobbyCommand;
public sealed record SetTeamAndScoreMode(int TeamMode, int ScoreMode, int? Size = null) : LobbyCommand;

public sealed record Say(string Text, CommandPriority SayPriority = CommandPriority.Info, string? CoalesceKey = null) : LobbyCommand
{
    public override CommandPriority Priority => SayPriority;
}

public sealed record Whisper(string Username, string Text) : LobbyCommand
{
    public override CommandPriority Priority => CommandPriority.Info;
}
