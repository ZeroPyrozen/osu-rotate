using OsuRotate.Domain;

namespace OsuRotate.Application;

// Everything the lobby actor reads from its inbox. Bancho events and lookup answers arrive on their
// own; the dashboard posts the owner's actions.
public abstract record LobbyInput;

public sealed record BanchoEvent(LobbyEvent Event) : LobbyInput;

public sealed record SkipHost : LobbyInput;

public sealed record SkipToPlayer(string Name) : LobbyInput;

// Names separated by commas, as *reorder takes them.
public sealed record ReorderQueue(string Names) : LobbyInput;

public sealed record DenyPlayer(string Name) : LobbyInput;

public sealed record AllowPlayer(string Name) : LobbyInput;

public sealed record ChangeSettings(LobbySettings Settings) : LobbyInput;

// Starting and aborting regardless of votes, as *start and *abort do. Zero seconds starts at once.
public sealed record ForceStart(int CountdownSeconds) : LobbyInput;

public sealed record ForceAbort : LobbyInput;

public sealed record CancelCountdown : LobbyInput;

// Lifecycle: make a new room, take over an existing one, close it now or once everyone has left.
public sealed record CreateLobby(string Title) : LobbyInput;

public sealed record AttachLobby(long RoomId) : LobbyInput;

public sealed record CloseLobby : LobbyInput;

public sealed record CloseLobbyWhenEmpty(bool Enabled) : LobbyInput;

// Kicking a player from the room, as the owner's !mp kick would.
public sealed record KickFromLobby(string Name) : LobbyInput;

// The owner typing in the lobby from the dashboard. Sent ahead of the bot's own announcements.
public sealed record SayInLobby(string Text) : LobbyInput;
