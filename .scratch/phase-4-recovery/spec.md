# Phase 4: persistence and recovery

Run the phase 3 engine for real: one actor owns it, feeds it Bancho events, timers and owner actions,
and carries out its commands. The lobby's session is saved to SQLite before commands leave, so a
restart rejoins the same room and restores the queue. Design: `docs/system-design.html` (figures 4,
7, 9 and 11).

Done when killing the process mid-match restores the same queue in the Bancho simulator.

## Decisions carried in

- One writer: every input goes through one `Channel`; the engine is touched only by the actor loop.
- State commits before the command leaves (figure 9). Votes and AFK points are not saved; they reset
  on restart by design.
- `LookUpBeatmap` goes to `IBeatmapCatalog`, never the transport (the Bancho formatter throws on it).
- Recovery waits for the channel JOIN before `!mp settings`; Bancho silently ignores it before then.
- `current_game_id` from the match API cannot decide alone whether a match is running; check the
  game's end time too.
- Store match history and stats only. Chat is never written to disk.

## Issues

01 is a prerequisite for the rest. 06 needs 02 to 05. 08 can start once 01 to 03 are done.
