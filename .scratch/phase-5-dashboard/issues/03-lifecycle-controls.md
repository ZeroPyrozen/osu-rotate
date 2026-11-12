# Lifecycle controls

Status: done

Blocked by: 02

UC1 to UC3. Create a lobby (title from the kept title or a form), attach to a room by id or
`https://osu.ppy.sh/mp/<id>` URL, close now (with a confirmation), and close when empty (and cancel
it). Buttons only offer what the current status allows.

Done when each control posts the right input and the page shows the resulting status.

## Outcome

- `Components/Lobby/LifecycleActions.cs` holds the rules and posts the inputs: create and attach
  only when Idle with no room, close in any state but Idle and Closing (which also stops a create or
  attach still waiting for Bancho), close-when-empty only when Open. Each action checks the lobby's
  state again before posting, because the page may be showing an older snapshot, and returns a
  message to show instead of throwing.
- Create takes a title (trimmed, at most 50 characters) or falls back to the kept title, then
  "osu-rotate". Attach takes a room number, `#number`, or an `osu.ppy.sh/mp/` or
  `/community/matches/` link.
- `Components/Lobby/LifecycleControls.razor` shows only the buttons the current state allows, asks
  for confirmation before closing ("Everyone in it is sent out"), and toggles close-when-empty.

Tests: room parsing, every state's allowed actions, each action reaching the engine against a real
actor (`TestLobby` helper), refusals for stale pages and a stopped bot, and the rendered buttons per
state. All 12 mutants are caught. Clicking through in a browser is not tested (no bUnit); the
component only calls `LifecycleActions`.
