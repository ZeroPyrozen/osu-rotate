# Live lobby view

Status: done

Blocked by: 01

UC4. One page that follows `LobbyRuntime` snapshots as they change: status and room (with a link to
the room on osu.ppy.sh), players with slot, team and match status, host, queue in order, current
beatmap, match running, referees, close-when-empty, and whether the bot is connected. Updates push to
the browser without polling.

Done when a bUnit or host test shows a snapshot change reaching the page, and a slow page cannot
slow the actor.

## Outcome

- `LobbySnapshot` now carries `Connected` (from `LobbyLifecycle.Connected`), since Idle alone does
  not say whether Bancho is reachable.
- `LobbyRuntime.Changed` raises each new snapshot, and null when the bot stops. It follows whichever
  actor is attached and drops the old one.
- `Components/Lobby/LobbyView.razor` renders one snapshot: status, Bancho connection, room with a
  link to osu.ppy.sh, close-when-empty, match state and map link, the queue in order with the host
  marked, players with slot, team, state, profile link and host and referee tags, and the referees.
  Names and titles are rendered as text.
- `Pages/Home.razor` shows it, listening to `LobbyRuntime.Changed` and handing each snapshot to its
  circuit with `InvokeAsync`, which only queues the render. It stops listening when the page closes.

Tests render with ASP.NET Core's `HtmlRenderer` (no bUnit): the view's contents, escaping, empty
and not-started states, the page following a real actor as Bancho events arrive and the bot
stopping, the page unsubscribing, a blocked page dispatcher not holding up the actor, and a stopped
actor no longer reaching pages. All 10 mutants are caught.

Not built yet: styling. The page is plain HTML; issue 08 or a later pass can make it look good.
