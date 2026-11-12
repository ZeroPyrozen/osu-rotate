# Match history

Status: done

Blocked by: 01, 03

Save each match with its scores (Match, MatchScore) when it finishes or is aborted: beatmap, host,
start and end, outcome. This is the history and stats the dashboard shows in phase 5. The engine
needs to report finished matches so the actor can save them.

Done when a played and an aborted match are saved with the right host, map and scores, and a
restart keeps them.

## Outcome

The engine reports each match that ends with a new application-handled command,
`RecordMatch(MatchRecord)` (`LobbyEngine.History.cs`): beatmap, the host who started it (with their
osu! user id when known), start and end times, Finished or Aborted, and the scores Bancho reported,
in order. A score reported twice counts once with the latest; players who never finished have none.
Like `LookUpBeatmap`, the Bancho formatter refuses it.

`IMatchArchive` (Application) saves and lists matches; `SqliteLobbyStore` implements it in the same
database with a second migration (`AddMatchHistory`): Match (linked to the open session of its room,
outcome stored by name), MatchScore (keyed by match and position) and Player (osu! user id, the name
last used, first and last seen). Players without a known id are kept in the scores by name only.

The actor saves recorded matches before sending the commands that came with them. A match that
fails to save is reported, kept, and saved with the next input. With no archive, matches are
dropped, never sent.

Tests: 10 engine tests (including a missed match end and a repeated one), 9 archive tests on real SQLite files (round trip, aborted with no host,
newest first, session link and no link to a closed session, player names, surviving the lobby closing and a reopen), 3 actor tests
and a formatter guard. All 18 mutants that applied are caught.
