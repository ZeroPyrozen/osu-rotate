# The `!pick` command in the engine

Status: done

`!pick` from a player, while the setting from issue 02 is on and no match is running, emits a new
command (e.g. `PickBeatmap(requester, regulation limits, oldest year)`) that the actor routes to
the pool and never to Bancho, the way `LookUpBeatmap` is. The answer comes back as an event (e.g.
`BeatmapPicked`). Silently ignored: when off, during a match, and within the cooldowns (30 s per
player, 10 s for the room, using `CooledDown`). The stored limits are used even while the
regulation is turned off.

On the answer:
- From the current host, with no match running: record the map as the last accepted map, then
  `SetBeatmap` with the room's mode and the `DescribeMap` description. Its own `BeatmapChanged`
  must not trigger a second lookup.
- From anyone else: announce `Suggestion from <player>: ` + `DescribeMap`.
- If the requester is no longer host, or a match started, drop it (a host's pick is not turned
  into a suggestion).
- None fit: "No maps fit the current rules." Failed: "Couldn't reach osu! right now, try again later."

`!help`/`!info` mention `!pick` only while it is on. Add the command to `LobbyTrace`.

Done when engine tests cover each line above, against the fake clock.

## Outcome

`src/OsuRotate.Domain/LobbyEngine.Pick.cs`, with 15 tests in `MapPickTests` and the harness taught
to answer picks (`PickResults`, `AnswerPicksAtOnce`/`AnswerPicks`).

- `!pick` and `!suggest` emit `PickBeatmap(Requester, AsHost, Query)`; the answer is
  `BeatmapPicked(Request, Result)`, which carries the request back so the engine needs no
  bookkeeping of its own. `Query` is `MapPickQuery.For(regulation, pick settings)`, so the stored
  limits apply even while the regulation is off.
- Cooldowns use the engine's `CooledDown` keys `pick` (room, 10 s) and `pick:<player>` (30 s).
  Both are checked before either is used up, so a pick refused by the room's cooldown doesn't
  start the player's.
- The host's answer is recorded as the last accepted map before `SetBeatmap`, so its own change
  isn't looked up again; it also becomes the `!mirror` map. Auto start is begun only with the
  regulation on, because with it off the map change starts it already (tested: one wait, not two).
- An answer is dropped when picks were turned off, a match started, a host's request comes back
  after the host changed, or the map no longer fits the rules as they are now.
- `!help`/`!info` gets one extra line about `!pick` while it is on, after the owner's own text.
- `LobbyTrace` describes both the command and the answer.

**Until issue 05:** the actor doesn't route `PickBeatmap` yet, so it would reach the Bancho
formatter, which throws for it. It can't happen in practice: picks are off by default and the
dashboard can't turn them on until issue 06.
