# Map pool over osu! search

Status: done

Blocked by: 01

A new Application port, e.g. `IMapPool`, asked for one map given the regulation's limits, the
mode, the oldest year and a set of map ids to skip. It answers a map, "none fit", or "failed"
(never throws), like `IBeatmapCatalog`.

The Infrastructure side searches osu! with the limits as filters, ranked and approved only, in
the room's mode only (no converts), keeps every fitting difficulty in memory, and draws at random
without handing out the same map twice. It refills below 10 left, one or two pages per fill. Issue 01
found paging is by `cursor_string` only, so for variety each fill picks a random sort order
(`ranked_desc`, `plays_desc`, `favourites_desc`, `difficulty_asc`, ...) and skips a random 0 to 3
pages along the cursor, within the rate limit and drops everything when the
limits, mode or year change. Nothing is written to disk. Search filters match whole sets (issue 01: only about a third of
returned difficulties fit), so every difficulty is checked for mode and rated with
`MapRegulation.Rate` before it enters the pool.

Uses `OsuApiClient`'s throttling and token. Done when tests with a fake HTTP handler cover: a
fill, drawing without repeats, skipped ids, a refill, a change of limits, an empty result and an
API failure.

## Outcome

- `MapPickQuery` and `MapPickResult` (`src/OsuRotate.Domain/MapPick.cs`): the query is the
  regulation's mode, star and length limits plus the oldest year, built with
  `MapPickQuery.For(regulation, pickSettings)`; `Fits` rates a map with `MapRegulation.Rate`.
  The engine (issue 04) uses these for its command and event.
- `IMapPool` (`src/OsuRotate.Application/IMapPool.cs`): `DrawAsync(query, skip, ct)`.
- `OsuMapPool` (`src/OsuRotate.Infrastructure/OsuApi/OsuMapPool.cs`), with 15 tests in
  `MapPoolTests`.

How a fill works: a random sort from `ranked_desc`, `ranked_asc`, `plays_desc`,
`favourites_desc`, `difficulty_asc`, `difficulty_desc`, then 0 to 3 pages skipped along the cursor,
and the next page kept, so 1 to 4 requests. If the results end, or a later page fails, before the
kept page, it keeps what it saw, so narrow rules still get their few maps. Only the first page
failing is `Failed`. Each difficulty is checked for mode, ranked/approved status, convert flag and
the limits, and each set for its ranked year.

**Changed from the plan:** the pool refills only when it is empty, not below 10 left. With a
narrow query that has fewer than 10 maps, "below 10" would search on every pick. A page gives 70
to 200 maps, so a refill comes about every 100 picks. When every map a refill finds was already
handed out, the round starts over rather than answering "none fit".

Not seen live: the `ranked_asc`, `favourites_desc` and `difficulty_desc` sorts, and whether the
search's difficulties carry `mode_int` (it reads `mode_int` if there, else `mode`). Issue 07 covers these.
