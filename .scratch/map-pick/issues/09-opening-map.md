# A new room opens with no map the bot chose

Status: done, not yet tried on the live lobby

Found by the owner on the live lobby, 2026-11-17: a room the bot makes (the first one, or one made
again after a close) has no map the bot picked. The first map is whatever the first host has, and
with no accepted map yet a map outside the rules has nothing to be reverted to, so it stays in the
room (only its start is refused).

## Fix

On `RoomCreated` the engine sends `ChooseOpeningMap` with the `!pick` query (the regulation's limits
and mode, the oldest ranked year), whether converts count, and whether the pool may be used (`!pick`
turned on). The actor answers with `OpeningMapChosen`:

1. The newest finished maps from the match history (unsaved ones first, then the archive's last 20
   matches), at most 10 distinct maps, each looked up in the lobby's mode. The first that fits wins.
   Aborted matches don't count.
2. If none fits and `!pick` is on, a map from the pool, skipping those recent maps.
3. Otherwise none, and the room stays as it was before this change.

The engine checks the answer against the rules as they are by then, and:

- If no map has changed in the room yet, or the room holds a rejected map, it sets the map as
  accepted (like a host's `!pick`, so it isn't looked up again).
- If a host's map is still being checked, that map is left alone, but the opening map becomes the
  map a rejection goes back to.
- If a host's map is already in (accepted, or any map with the regulation off), or a match is on, the
  answer is dropped.

The opening map doesn't start auto start: it waits for the first host to pick or start.

Rejoining a room after a restart is unchanged. That room keeps its map, which is checked quietly as
before.

Tests: `OpeningMapTests` (domain), the opening map cases in `LobbyActorTests`, and the trace lines in
`LobbyTraceTests`.
