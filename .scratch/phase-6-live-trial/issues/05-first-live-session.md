# First live session

Status: ready-for-human

Blocked by: 04

The owner adds `Bancho__Nick` and `Bancho__Password` to the secrets file, restarts, and runs the
lobby from the dashboard for an evening with real players. The owner plays in it too.

Watch for, and record here:

- Creating the room, the title, size, mods and password the keeper sets.
- Rotation: the queue moves on match start, the host passes on finish, an aborted match keeps the
  host, a manual host transfer is put back.
- Votes (skip, abort, start), the AFK checks, auto start, and the stuck match abort.
- Map checks against the real osu! API: star rating and length, a converted map in another mode
  (the converts list), a map that isn't found.
- Match history: scores and the `pass` field, as shown on `/history`.
- Chat and moderation from the dashboard, the info commands (`!rank`, `!mirror`, `!update`), and
  the chat rate (no Bancho flood warnings).
- The owner is never AFK-kicked.

The agent reads the journal and a copy of the database the owner shares (never the secrets file)
and compares what happened with what the engine should have done.

Done when every item is recorded as working, or has its own fix issue.

## Comments

2026-11-13: before the session, the journal only had errors, so most of the list above couldn't be
checked afterwards. The owner chose to add a trace: `LobbyTrace` describes every input the lobby
takes and every command it sends, and `LoggingLobbyDiagnostics` writes them at Information as
"Lobby: ..." and "Bot: ...". Player chat becomes "chat from <name>" plus its `!command` word; the
owner's own chat is only counted; the room password is never shown; countdown ticks are left out.
The agent reads it with `journalctl -u osu-rotate` (the owner's user is in `adm`), no sudo needed.

