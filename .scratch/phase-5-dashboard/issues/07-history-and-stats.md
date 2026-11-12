# History and stats

Status: done

Blocked by: 01

UC9. Recent matches (map, host, time, outcome, scores) from `IMatchArchive`, with paging, and simple
stats: most played maps, most active players, matches per day.

Done when the pages read a seeded database correctly and stay fast with a year of matches.

## Outcome

- `IMatchArchiveReader` (Application) beside the archive: `PageAsync` (newest first, keyed on the
  match id, so new matches never shift an older page) and `StatsAsync` over a window (most played
  maps, most active players, matches per day in a given time zone offset). `MatchQueries` gives the
  same answers in LINQ for `InMemoryMatchArchive`, and the SQLite store is tested to agree with it.
- `SqliteLobbyStore` implements it. Times are now saved in UTC, so the stored text sorts by time;
  migration `AddMatchStartedIndex` indexes `Match.StartedAt`. The stats use raw SQL because EF cannot
  compare or group SQLite's DateTimeOffset text. Players are told apart by osu! user id when known
  (shown by the name last seen) and guests by name.
- The store is now one DI singleton shared by the bot and the pages, so the history is readable
  even when the bot is not running.
- `/history` (owner only, linked from the lobby page): stats over 7, 30, 90 or 365 days with a bar
  per day, and recent matches 25 at a time with Older, Newer and Newest. Each match shows its map
  (linked to osu!), host, outcome and scores, best first; names are shown as text. A database error
  shows a message instead of breaking the page. Maps are shown by beatmap id: the history does not
  store titles.

Speed, with a year of a busy lobby seeded (131,400 matches, 1M scores, about 75 MB), on the dev
machine: a page 30 ms, a month of stats 160 ms, a whole year 1.1 s. Most of the year is SQLite
grouping a million scores. The first version took 1.75 s; walking the window's matches by time
(`CROSS JOIN`) and grouping before looking up names halved it. Left to itself, SQLite picked the
per-player index and took 11 s. A covering index saved only another 25% for 27% more disk, so it
was left out. Not yet measured on the Pi (phase 6); if the year view is too slow there, a daily
summary table would fix it.

Tests: the stats and pages on a seeded database, against hand-worked answers and the LINQ
reference (renames, guests, an aborted match, days either side of midnight in UTC and UTC+7), UTC
storage, the year-sized timing test, the pager, rendering (markup shown as text, empty and failing
history), and the page behind the owner login reading the real database.

Mutation check: 26 mutants. 23 caught at once. One gap was real (nothing checked that a host's
newer name wins) and has a test now. Two survivors were equivalent: SQLite reads "420 minutes" as
"+420 minutes" (the sign format was removed), and `/history`'s owner attribute repeats the fallback
policy, which already requires the owner everywhere (kept, as on the lobby page).
