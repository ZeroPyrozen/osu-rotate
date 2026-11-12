# Recovery after restart

Status: done

Blocked by: 02, 03, 04, 05

On start with a saved session: rejoin the room, wait for the JOIN, load `!mp settings` and the
referees, reconcile the saved queue with who is there, and check from the match API whether a match
is running (current game and its end time). Restore the engine's queue and host, then continue.

Done when killing the process mid-match restores the same queue in the Bancho simulator, which is
the phase's done criterion.

## Outcome

Done criterion met: in the Bancho simulator, killing the process mid-match and starting a new one on
the same database rejoins the same room, restores the queue p2, p3, p1 with p1 still hosting the
running match, hands host to p2 when it finishes, and records that match with its real start time.

How it works:

- `LobbyRecovery.RecoverAsync` (Application) loads the saved lobby, rebuilds the engine with the
  saved rules (owner and version still come from the host), restores the deny list, and for a saved
  room asks `IMatchHistory` whether a game is running (current game with no end time). The
  lifecycle starts with the saved room, so it rejoins on connect and remakes the room if it is gone.
  No answer from the API (no key, down, or the room gone) counts as no match: that only hands host
  on a little early, whereas guessing a match that has ended would leave the lobby waiting.
- `LobbyEngine.RestoreOnNextRoomState` applies the saved queue on the first `!mp settings` after
  rejoining, instead of rebuilding it in slot order, because mid-match the queue has already rotated.
  Players who left are dropped, newcomers follow in slot order, denied players stay out. With a match
  running, the room resumes it: host stays until it finishes, then the front of the queue hosts, and
  the match is recorded with the API's start time and map. With no match, the front gets host at
  once. A match that ends before the room state arrives is not resumed.
- The actor does not save while a restore is pending, so a second crash in that window cannot
  overwrite the saved queue with a partial one.
- Votes and AFK points start fresh, as designed.

Tests: 17 engine tests, 9 startup tests, an actor test for the no-save window, and 5 simulator tests
(kill mid-match with the match still running, a match that ended while the bot was down, players who
came and went, a room that closed while down, nothing saved), stable over 10 repeated runs. All 24
mutants are caught; one removed a line that did nothing (resetting the map picker on a fresh engine).

The simulator learned `!mp host #id`, and now closes the connection for a wrong password the way
Bancho does, after the last line has gone out. A plain close could reset the connection on Windows
and lose the rejection lines, which is the likely cause of a probe test from phase 2 failing once
under load; it passed in every run since.
