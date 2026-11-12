# Chat and moderation

Status: done

Blocked by: 02

UC7. Show the lobby's chat live (kept in memory, the last few hundred lines, never on disk), send
chat as the owner, kick a player, and add or remove players from the deny list (with the list shown).
Player text is shown as text, never as HTML.

Done when sent chat goes out at Admin priority, chat from Bancho appears, a kick and a deny take
effect, and a chat line with markup is shown escaped.

## Outcome

- `ChatLog` (Application) keeps the last 300 lines in memory, never on disk. The actor adds players'
  chat as it reaches the engine, each line of the bot's own announcements (they go out from the
  owner's account, so they are marked as the bot's), and what the owner sends from the dashboard.
  `LobbyRuntime` exposes it and raises `ChatAdded` for pages.
- New owner input `KickFromLobby`, sent straight to Bancho as `!mp kick`. BanchoBot's "Kicked X"
  reply then marks it as a referee kick, so the host-kick counterattack does not count it.
- `ModerationActions`, `ChatPanel.razor` (live chat, send box) and `ModerationPanel.razor` (kick with a
  confirmation, never-host per player or by name, the never-host list with allow-again).
- Chat is trimmed and limited to 400 characters. A message starting with `!` still goes out as
  "Bot: ..." (phase 2's guard), so the dashboard cannot run `!mp` commands through chat.

Tests: chat log, what the actor logs and sends, the actions against a real actor, and rendering:
live chat, chat from before the page opened, the bot marked, markup shown as text, and the
moderation lists. All 17 mutants are caught (one had to be applied by hand: my sed patterns could
not express a `'\n'` in C#).
