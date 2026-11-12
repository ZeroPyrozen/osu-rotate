# Queue and host controls

Status: done

Blocked by: 02

UC5, UC6. Reorder the queue (move up and down, or drag), skip the host, give host to a player, start
the match now or with a countdown, and abort. These force the action regardless of votes. The
engine needs `StartMatch` and `AbortMatch` owner inputs, as the `*start` and `*abort` chat commands
already do.

Done when each control's input reaches the engine and its effect shows in the next snapshot.

## Outcome

- Engine (`LobbyEngine.Owner.cs`): `ForceStart(seconds)` (0 starts at once, up to 300 with the
  usual warnings), `ForceAbort()` and `CancelCountdown()`, the same as `*start`, `*abort` and `!stop`.
  New owner inputs `ForceStart`, `ForceAbort`, `CancelCountdown`; the snapshot now has
  `CountdownRunning`, and the view says when the countdown is running.
- `Components/Lobby/QueueActions.cs` and `QueueControls.razor`, shown only while the room is open:
  start now or with a countdown, stop the countdown, abort the match, skip the host, and a queue
  editor where each player moves up or down one place or gets host. Moves post the whole new
  order through `ReorderQueue`, so they go through the same validation as `*reorder`.

Tests: 9 engine tests, and 41 dashboard tests (rules, actions against a real actor, rendering per
state). All 17 mutants are caught. Drag and drop was left out: up and down buttons work without
JavaScript and on a phone.
