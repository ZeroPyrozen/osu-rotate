# The `!pick` command in the engine

Status: ready-for-agent

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
