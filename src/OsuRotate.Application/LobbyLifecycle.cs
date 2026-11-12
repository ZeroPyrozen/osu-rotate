using OsuRotate.Domain;

namespace OsuRotate.Application;

// The lobby's state machine (design figure 7): whether there is a room, and getting into it.
// Idle -> Connecting -> Creating or Rejoining -> Syncing -> Open, Reconnecting after a dropped
// connection, Closing when the owner closes. It decides which Bancho events reach the engine and
// when the engine starts over for a new room. Pure: the actor feeds it and carries out the result.
public sealed class LobbyLifecycle
{
    public const string DefaultTitle = "osu-rotate";

    private bool _connected;
    private bool _createWhenConnected;
    private bool _swallowJoin;
    private bool _recreateIfGone;
    private bool _closeWhenEmpty;
    private bool _closedOnPurpose;

    // A saved session from before a restart: rejoin it, and recreate it if it is gone.
    public LobbyLifecycle(long? savedRoomId = null, string? savedTitle = null)
    {
        RoomId = savedRoomId;
        Title = savedTitle;
        _recreateIfGone = savedRoomId is not null;
        Status = savedRoomId is null ? LobbyStatus.Idle : LobbyStatus.Connecting;
    }

    public LobbyStatus Status { get; private set; }
    public long? RoomId { get; private set; }
    public string? Title { get; private set; }
    public bool ClosingWhenEmpty => _closeWhenEmpty;

    public LifecycleStep OnInput(LobbyInput input, LobbySettings settings) => input switch
    {
        CreateLobby e => Create(e.Title),
        AttachLobby e => Attach(e.RoomId),
        CloseLobby => Close(),
        CloseLobbyWhenEmpty e => CloseWhenEmpty(e.Enabled, settings),
        _ => LifecycleStep.None,
    };

    public LifecycleStep OnEvent(LobbyEvent lobbyEvent, LobbySettings settings)
    {
        switch (lobbyEvent)
        {
            case TransportConnected:
                _connected = true;
                return OnConnected();

            case TransportDisconnected:
                _connected = false;
                return OnDisconnected();

            case TransportAuthenticationFailed:
                // The transport has stopped for good. The session stays for the next start.
                _connected = false;
                _createWhenConnected = false;
                Status = LobbyStatus.Idle;
                return LifecycleStep.None;

            // Creating a room also joins its channel. Only RoomCreated reaches the engine, once.
            case RoomJoined e when _swallowJoin:
                _swallowJoin = false;
                RoomId ??= e.RoomId;
                return LifecycleStep.None;

            case RoomJoined e when Status == LobbyStatus.Rejoining && e.RoomId == RoomId:
                // Bancho ignores !mp settings until the channel is joined, so it is asked for now.
                Status = LobbyStatus.Syncing;
                return new LifecycleStep([new RequestRoomState()], Forward: true);

            case RoomJoined:
                return LifecycleStep.None;

            case RoomCreated e when Status == LobbyStatus.Creating:
                RoomId = e.RoomId;
                Title = e.Title;
                Status = LobbyStatus.Open;
                return LifecycleStep.Pass;

            case RoomCreated:
                return LifecycleStep.None;

            case RoomJoinFailed e when Status == LobbyStatus.Rejoining && e.RoomId == RoomId:
                return OnRoomGone();

            case RoomJoinFailed:
                return LifecycleStep.None;

            case RoomStateReceived e when Status == LobbyStatus.Syncing:
                Status = LobbyStatus.Open;
                Title = e.State.Name;
                return LifecycleStep.Pass;

            case RoomCloseAcknowledged when RoomId is not null:
                // BanchoBot answers a referee's !mp close with this: someone closed it on purpose.
                _closedOnPurpose = true;
                return OnClosed();

            case RoomClosed when RoomId is not null:
                return OnClosed();

            case RoomCloseAcknowledged or RoomClosed:
                return LifecycleStep.None;

            case ChatReceived e when RoomId is not null:
                // The owner closing the room in-game. The engine still sees the chat.
                if (string.Equals(PlayerNames.ToIrcName(e.Username), PlayerNames.ToIrcName(settings.OwnerName), StringComparison.OrdinalIgnoreCase)
                    && e.Text.Trim().StartsWith("!mp close", StringComparison.OrdinalIgnoreCase))
                {
                    _closedOnPurpose = true;
                }

                return LifecycleStep.Pass;

            default:
                return RoomId is not null ? LifecycleStep.Pass : LifecycleStep.None;
        }
    }

    // Called after the engine has handled an event, with the players now in the room.
    public LifecycleStep AfterEngine(int players)
    {
        if (_closeWhenEmpty && Status == LobbyStatus.Open && players == 0)
        {
            return Close();
        }

        return LifecycleStep.None;
    }

    private LifecycleStep Create(string title)
    {
        if (RoomId is not null || Status is LobbyStatus.Creating or LobbyStatus.Closing)
        {
            return LifecycleStep.None;
        }

        Title = string.IsNullOrWhiteSpace(title) ? DefaultTitle : title.Trim();
        _recreateIfGone = true;
        if (!_connected)
        {
            _createWhenConnected = true;
            Status = LobbyStatus.Connecting;
            return LifecycleStep.None;
        }

        return SendCreate();
    }

    private LifecycleStep SendCreate()
    {
        _createWhenConnected = false;
        _swallowJoin = true;
        _closeWhenEmpty = false;
        _closedOnPurpose = false;
        RoomId = null;
        Status = LobbyStatus.Creating;
        return new LifecycleStep([new CreateRoom(Title ?? DefaultTitle)], ResetEngine: true);
    }

    private LifecycleStep Attach(long roomId)
    {
        if (RoomId is not null || Status is LobbyStatus.Creating or LobbyStatus.Closing)
        {
            return LifecycleStep.None;
        }

        RoomId = roomId;
        _createWhenConnected = false;

        // The owner picked this room: if it is gone, say so rather than make another.
        _recreateIfGone = false;
        if (!_connected)
        {
            Status = LobbyStatus.Connecting;
            return new LifecycleStep([], ResetEngine: true);
        }

        Status = LobbyStatus.Rejoining;
        return new LifecycleStep([new JoinRoom(roomId)], ResetEngine: true);
    }

    private LifecycleStep Close()
    {
        if (RoomId is null)
        {
            // Nothing made yet: just stop trying.
            _createWhenConnected = false;
            Status = LobbyStatus.Idle;
            return LifecycleStep.None;
        }

        if (!_connected)
        {
            return Forget();
        }

        _closedOnPurpose = true;
        Status = LobbyStatus.Closing;
        return new LifecycleStep([new CloseRoom()]);
    }

    // osu-ahr's LobbyTerminator: lock newcomers out, say why, close when the last player leaves.
    private LifecycleStep CloseWhenEmpty(bool enabled, LobbySettings settings)
    {
        if (Status != LobbyStatus.Open || enabled == _closeWhenEmpty)
        {
            return LifecycleStep.None;
        }

        _closeWhenEmpty = enabled;
        if (!enabled)
        {
            return new LifecycleStep([new SetPassword(settings.Keeping.Password is { Length: > 0 } kept ? kept : null), new Say("This lobby will stay open.")]);
        }

        return new LifecycleStep(
        [
            new SetPassword(Guid.NewGuid().ToString("N")[..12]),
            new Say("This lobby will be closed when everyone leaves."),
            new Say("Thank you for playing with the auto host rotation lobby."),
        ]);
    }

    private LifecycleStep OnConnected()
    {
        if (Status == LobbyStatus.Reconnecting)
        {
            _recreateIfGone = true;
        }

        if (RoomId is { } roomId && Status is LobbyStatus.Connecting or LobbyStatus.Reconnecting)
        {
            Status = LobbyStatus.Rejoining;
            return new LifecycleStep([new JoinRoom(roomId)]);
        }

        if (_createWhenConnected)
        {
            return SendCreate();
        }

        return LifecycleStep.None;
    }

    private LifecycleStep OnDisconnected()
    {
        switch (Status)
        {
            case LobbyStatus.Closing:
                // The close may or may not have gone through; either way the owner wanted it gone.
                return Forget();
            case LobbyStatus.Creating:
                _swallowJoin = false;
                if (RoomId is null)
                {
                    _createWhenConnected = true;
                    Status = LobbyStatus.Connecting;
                    return LifecycleStep.None;
                }

                Status = LobbyStatus.Reconnecting;
                return LifecycleStep.None;
            case LobbyStatus.Rejoining or LobbyStatus.Syncing or LobbyStatus.Open:
                Status = LobbyStatus.Reconnecting;
                return LifecycleStep.None;
            default:
                return LifecycleStep.None;
        }
    }

    private LifecycleStep OnRoomGone()
    {
        if (_recreateIfGone)
        {
            return SendCreate();
        }

        return Forget();
    }

    private LifecycleStep OnClosed()
    {
        if (_closedOnPurpose || _closeWhenEmpty || Status == LobbyStatus.Closing)
        {
            return Forget();
        }

        // Closed by Bancho or someone else: the lobby is meant to stay open.
        return SendCreate();
    }

    private LifecycleStep Forget()
    {
        RoomId = null;
        _swallowJoin = false;
        _closeWhenEmpty = false;
        _closedOnPurpose = false;
        _createWhenConnected = false;
        _recreateIfGone = false;
        Status = LobbyStatus.Idle;
        return new LifecycleStep([], ResetEngine: true);
    }
}

// What the actor does after the lifecycle has looked at an input: send these commands, hand the
// event to the engine or not, and start the engine over (for a new or forgotten room) first.
public sealed record LifecycleStep(IReadOnlyList<LobbyCommand> Commands, bool Forward = false, bool ResetEngine = false)
{
    public static LifecycleStep None { get; } = new([]);
    public static LifecycleStep Pass { get; } = new([], Forward: true);
}
