# Map pool over osu! search

Status: ready-for-agent

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
