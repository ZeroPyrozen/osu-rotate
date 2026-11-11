namespace OsuRotate.Domain;

public enum Team
{
    None,
    Blue,
    Red,
}

public enum PlayerActivity
{
    None,
    Idle,
    Playing,
    Watching,
    Editing,
    Testing,
    Submitting,
    Modding,
    Multiplayer,
    Multiplaying,
    Afk,
    Unknown,
}

public sealed record RoomPlayer(int Slot, string ReadyState, int OsuUserId, string Username, bool IsHost, Team Team, string Options);

public sealed record RoomState(
    string Name,
    long RoomId,
    int BeatmapId,
    string BeatmapTitle,
    string TeamMode,
    string WinCondition,
    string ActiveMods,
    IReadOnlyList<RoomPlayer> Players);

public sealed record PlayerStat(string Username, int OsuUserId, PlayerActivity Activity, long Score, int Rank, int Plays, int Level, double Accuracy);

public abstract record LobbyEvent;

// Connection
public sealed record TransportConnected : LobbyEvent;
public sealed record TransportDisconnected(string Reason) : LobbyEvent;
public sealed record TransportAuthenticationFailed : LobbyEvent;

// Room lifecycle
public sealed record RoomCreated(long RoomId, string Title) : LobbyEvent;
public sealed record RoomJoined(long RoomId) : LobbyEvent;
public sealed record RoomJoinFailed(long RoomId, string Reason) : LobbyEvent;
public sealed record RoomClosed(long RoomId) : LobbyEvent;
public sealed record RoomCloseAcknowledged : LobbyEvent;
public sealed record RoomStateReceived(RoomState State) : LobbyEvent;
public sealed record RoomSizeChanged(int Size) : LobbyEvent;
public sealed record RoomSettingsChanged(int? Slots, string? TeamMode, string? WinCondition) : LobbyEvent;
public sealed record RoomLocked : LobbyEvent;
public sealed record RoomUnlocked : LobbyEvent;
public sealed record PasswordChanged : LobbyEvent;
public sealed record PasswordRemoved : LobbyEvent;

// Players
public sealed record PlayerJoined(string Username, int Slot, Team Team) : LobbyEvent;
public sealed record PlayerLeft(string Username) : LobbyEvent;
public sealed record PlayerMoved(string Username, int Slot) : LobbyEvent;
public sealed record PlayerTeamChanged(string Username, Team Team) : LobbyEvent;
public sealed record PlayerKicked(string Username) : LobbyEvent;
public sealed record PlayerInvited(string Username) : LobbyEvent;
public sealed record PlayerRolled(string Username, int Points) : LobbyEvent;
public sealed record PlayerStatReceived(PlayerStat Stat, bool ViaPrivateMessage) : LobbyEvent;
public sealed record RefereeAdded(string Username) : LobbyEvent;
public sealed record RefereeRemoved(string Username) : LobbyEvent;
public sealed record RefereeListStarted : LobbyEvent;

// Host
public sealed record HostChanged(string Username) : LobbyEvent;
public sealed record HostTransferAcknowledged(string Username) : LobbyEvent;
public sealed record HostCleared : LobbyEvent;
public sealed record UserNotFound : LobbyEvent;
public sealed record NoUserSpecified : LobbyEvent;

// Beatmap
public sealed record BeatmapChanging : LobbyEvent;
public sealed record BeatmapChanged(int BeatmapId, string Title, bool ChangedByReferee) : LobbyEvent;
public sealed record InvalidBeatmapId : LobbyEvent;
public sealed record InvalidSettings : LobbyEvent;
public sealed record InvalidSize : LobbyEvent;

// Match
public sealed record AllPlayersReady : LobbyEvent;
public sealed record CountdownStarted(int Seconds) : LobbyEvent;
public sealed record CountdownTick(int Seconds) : LobbyEvent;
public sealed record CountdownFinished : LobbyEvent;
public sealed record CountdownAborted : LobbyEvent;
public sealed record MatchStarted : LobbyEvent;
public sealed record MatchStartAcknowledged : LobbyEvent;
public sealed record MatchAlreadyStarted : LobbyEvent;
public sealed record PlayerFinished(string Username, long Score, bool Passed) : LobbyEvent;
public sealed record MatchFinished : LobbyEvent;
public sealed record MatchAborted : LobbyEvent;
public sealed record MatchNotInProgress : LobbyEvent;

// Chat
public sealed record ChatReceived(string Username, string Text, bool IsAction) : LobbyEvent;
public sealed record PrivateMessageReceived(string Username, string Text) : LobbyEvent;
