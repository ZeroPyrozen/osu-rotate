# Recovery after restart

Status: ready-for-agent

Blocked by: 02, 03, 04, 05

On start with a saved session: rejoin the room, wait for the JOIN, load `!mp settings` and the
referees, reconcile the saved queue with who is there, and check from the match API whether a match
is running (current game and its end time). Restore the engine's queue and host, then continue.

Done when killing the process mid-match restores the same queue in the Bancho simulator, which is
the phase's done criterion.
