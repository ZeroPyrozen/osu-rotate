# Lobby lifecycle

Status: done

Blocked by: 01

The state machine in figure 7: Idle, Connecting, Creating, Rejoining, Syncing, Open, Reconnecting,
Closing. The owner creates a lobby (from the saved settings), attaches to an existing room by id, or
closes it (now, or once it is empty, which replaces osu-ahr's LobbyTerminator). After a JOIN the
actor asks for `!mp settings` and `!mp listrefs` before play continues. A lost connection goes to
Reconnecting and rejoins; a room that is gone is recreated from the settings.

Done when simulator tests cover create, attach, close, close when empty, a dropped connection
mid-lobby, and a room that disappeared while the bot was away.

## Outcome

`LobbyLifecycle` (Application) is a pure state machine over the existing `LobbyStatus` values. The
actor asks it about every Bancho event and the new owner inputs `CreateLobby`, `AttachLobby`,
`CloseLobby` and `CloseLobbyWhenEmpty`; it answers with commands, whether the engine sees the event,
and whether the engine starts over (a new or forgotten room, keeping the settings). The snapshot now
carries the status, room id, title and close-when-empty flag.

- Creating a room both joins its channel and gets BanchoBot's reply, in either order. Only
  `RoomCreated` reaches the engine, so the keeper sets the room up once.
- Rejoining waits for the JOIN, then asks `!mp settings` (the engine asks `!mp listrefs`); the room
  is Open once the settings arrive.
- A dropped connection rejoins the same room and keeps the queue. A room gone on rejoin is made
  again with its title, except one the owner attached to by hand, which leaves the bot Idle.
- A saved session (constructor arguments, for issue 06) rejoins on start and is remade if gone.
- Closed on purpose (the dashboard, BanchoBot's "Closed the match", or the owner's own `!mp close`
  in the lobby) leaves the bot Idle. Closed any other way, the room is made again: the lobby runs
  24/7.
- Close when empty, from osu-ahr's LobbyTerminator: a random password locks newcomers out, the
  lobby is told, and `!mp close` goes once the last player leaves. Cancelling restores the kept
  password or opens the room.
- A wrong IRC password stops everything but keeps the session.

Differences from osu-ahr: the lock password is random instead of "closed", and the close is sent
as soon as the room is empty instead of after a timer.

Tests: 31 pure lifecycle tests, actor tests for the wiring, and 9 tests that run the actor, the real
IRC transport and the Bancho simulator together (create, attach, attach to a gone room, close, close
when empty, a dropped connection, a room gone while away, Bancho closing the room, the owner closing
in-game), stable over 10 repeated runs. All 30 mutants are caught. The simulator gained
`RemoveRoom` and `CloseRoomFromBancho`.
