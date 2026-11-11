# Lobby lifecycle

Status: ready-for-agent

Blocked by: 01

The state machine in figure 7: Idle, Connecting, Creating, Rejoining, Syncing, Open, Reconnecting,
Closing. The owner creates a lobby (from the saved settings), attaches to an existing room by id, or
closes it (now, or once it is empty, which replaces osu-ahr's LobbyTerminator). After a JOIN the
actor asks for `!mp settings` and `!mp listrefs` before play continues. A lost connection goes to
Reconnecting and rejoins; a room that is gone is recreated from the settings.

Done when simulator tests cover create, attach, close, close when empty, a dropped connection
mid-lobby, and a room that disappeared while the bot was away.
