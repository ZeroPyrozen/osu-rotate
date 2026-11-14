# Check that osu! search works with the bot's token

Status: done

The bot calls the osu! API with an app-only token (`client_credentials`, scope `public`, see
`OsuApiClient`). The whole feature assumes `GET /api/v2/beatmapsets/search` answers that token.

An agent first adds a search mode to the probe in `tools/` that prints, for one query, the HTTP
status, the total, the cursor, and per set: id, status, ranked date, and each difficulty's mode,
star rating and total length. The query to try:
`q=stars>=4 stars<=5.5 length<=300 ranked>=2012`, `m=0`, `s=ranked`, and once with `sort=plays_desc`.

The owner runs it with the app credentials from the Pi's secrets file and pastes the output here.

Done when we know whether search works with the app token, which filters it honours (`stars`,
`length`, `ranked`), what one page holds, and how paging works. If it refuses the token, stop and
re-plan: the feature would need a user token.

## Comments

**2026-11-14, agent:** the probe is ready: `tools/OsuRotate.BanchoProbe/SearchProbe.cs`, run with
`--search`. It needs only the OAuth client id and secret (prompted, or from
`OSU_ROTATE_CLIENT_ID` / `OSU_ROTATE_CLIENT_SECRET`), no IRC and no room, and only reads. It runs
the query above on the first page, the cursor's second page, `sort=plays_desc` and
`sort=difficulty_asc`, and for each counts how many returned difficulties break each filter. The
report goes to `artifacts/probe/search-*.md` and never contains the token or secret. Tested
against a fake osu! (`SearchProbeTests`). Owner: run it and paste the report below.

**2026-11-14, owner ran it** (`artifacts/probe/search-20261114-213312.md`, not committed). Findings:

- **The app token works.** Token issued, and all four searches answered HTTP 200. No user token needed.
- **Filters match sets, not difficulties.** A set comes back if any one difficulty fits, with all
  its difficulties. Only 26-38% of returned difficulties fit every limit (83 of 263 on the first
  page), almost all misses being star rating. The pool must filter each difficulty itself.
- **`length<=300` and `ranked>=2012` held**: no long difficulty, no set ranked before 2012.
- **`m=0` doesn't exclude other modes inside a set**: hybrid sets bring taiko/fruits/mania
  difficulties (4 to 30 per page). Filter on each difficulty's `mode`.
- **`s=ranked`** returned only `ranked` sets and difficulties. Whether it includes `approved` wasn't seen.
- **Paging is by `cursor_string`**, 50 sets a page. `total` read 10000 every time, so it's a cap,
  not a count, and can't be used to pick a random page.
- **One page gives 70 to 200 fitting difficulties**, so one or two pages fill the pool.
- **Default sort is newest ranked first**, so it leans to this month's maps. Other sorts
  (`plays_desc`, `difficulty_asc`) give older, different sets.
